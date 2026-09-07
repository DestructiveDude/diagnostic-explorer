using DiagnosticExplorer.Logging;

namespace Diagnostic.Service.ClientHandlers;

/// <summary>
///     The service's own copy of one process's log stream, held so a browser attaching later can
///     be given the history rather than an empty grid.
/// </summary>
/// <remarks>
///     <para>
///         The agent already retains events, but only the agent's live subscription receives them:
///         a browser that opens the process a minute in, or reloads, would otherwise see nothing
///         until the next event happens to arrive. Keeping a relayed copy here means every web
///         client starts from the same picture, and a reconnecting agent merges into it instead of
///         replacing it.
///     </para>
///     <para>
///         Events are keyed on <see cref="LogStreamEvent.Sequence" />, which is what makes the
///         merge idempotent: a reconnect replays events this store has already seen, and they are
///         dropped rather than duplicated. A different <see cref="LogStreamEvent.StreamId" /> means
///         the agent process restarted and its sequence numbers began again, so the history is
///         discarded rather than interleaved with numbers that no longer mean the same thing.
///     </para>
/// </remarks>
internal sealed class LogEventRelayStore
{
    private readonly Dictionary<long, LogStreamEvent> _events = [];
    private readonly Lock _sync = new();
    private long _highWatermark;
    private LogEventRetentionOptions _retention = new();
    private LogStreamRoutingConfiguration _routing = new();
    private string? _streamId;

    /// <summary>Takes an agent's initialization snapshot.</summary>
    /// <returns>
    ///     True when this replaced a different stream, so the caller knows the history it held is
    ///     gone rather than added to.
    /// </returns>
    public bool MergeInitialization(LogStreamInitialization initialization)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        if (string.IsNullOrWhiteSpace(initialization.StreamId))
        {
            throw new ArgumentException("A log stream initialization requires a stream ID.", nameof(initialization));
        }

        lock (_sync)
        {
            // The stream id is the only restart signal, deliberately.
            //
            // A watermark below the one held looks like a restarted process whose sequence counter
            // began again, and this used to treat it as one. But it is indistinguishable from a
            // STALE initialization: an agent's previous send loop can still have an
            // InitializeLogStream in flight when a new one starts, and the hub runs several
            // invocations per connection concurrently, so the older one can land second carrying
            // an older watermark. Clearing on that wipes the relay and every browser is sent an
            // empty snapshot — and since the initialization no longer carries its own replay, the
            // wipe sticks until the next subscribe cycle. Merging a stale one instead costs
            // nothing, because it is keyed by sequence like everything else.
            //
            // Nothing is lost by dropping the restart case: every agent's id is a fresh Guid per
            // process (LogEventStore's constructor), so a real restart always arrives with a
            // different id. A caller that supplies its OWN fixed id and restarts would merge the
            // two incarnations, which is the documented cost of choosing a fixed id.
            var streamReplaced = !string.Equals(_streamId, initialization.StreamId, StringComparison.Ordinal);
            if (streamReplaced)
            {
                _events.Clear();
                _highWatermark = 0;
                _streamId = initialization.StreamId;
            }

            _routing = (initialization.Routing ?? new LogStreamRoutingConfiguration()).Clone();
            _retention = GetRetention(initialization);
            Merge(initialization.ReplayEvents);
            _highWatermark = Math.Max(_highWatermark, initialization.HighWatermark);
            Prune();
            return streamReplaced;
        }
    }

    /// <summary>Adds live events.</summary>
    /// <returns>Only those not already held, in sequence order, so the caller relays each once.</returns>
    public LogStreamEvent[] Append(IEnumerable<LogStreamEvent> events)
    {
        lock (_sync)
        {
            // Events that arrive before any initialization belong to a stream this store cannot
            // identify, so there is nothing to key them against. Dropping them is safe: the
            // initialization that follows carries the agent's own replay of the same events.
            if (string.IsNullOrWhiteSpace(_streamId))
            {
                return [];
            }

            var added = Merge(events);
            Prune();
            return added;
        }
    }

    /// <summary>The snapshot handed to a web client when it attaches.</summary>
    public LogStreamInitialization CreateInitialization()
    {
        lock (_sync)
        {
            Prune();
            return new LogStreamInitialization
            {
                StreamId = _streamId ?? string.Empty,
                Routing = _routing.Clone(),
                ReplayEvents = [.. _events.Values.OrderBy(streamEvent => streamEvent.Sequence)],
                HighWatermark = _highWatermark,
                MaxEvents = _retention.MaxEvents,
                MaxAgeMinutes = _retention.MaxAgeMinutes,
            };
        }
    }

    /// <summary>Reads the retention an agent asked for, defaulting and clamping what it sent.</summary>
    /// <remarks>
    ///     These values arrive over the wire, so they are an agent's claim rather than this
    ///     service's configuration. A zero means "not stated", not "retain nothing". The upper
    ///     bound is the one that matters: Prune does TimeSpan.FromMinutes on this, which throws for
    ///     infinity or anything past TimeSpan's range, and it would throw AFTER the value had been
    ///     stored — so every later append, merge and snapshot for that process would throw too, and
    ///     AddWebClient would fault inside the subscription lock. One malformed number would make a
    ///     process unwatchable until the service restarted. The agent applies the same rule to its
    ///     own configuration in LogEventRetentionOptions.CloneAndValidate; the difference is that a
    ///     misconfigured agent should fail loudly at startup, whereas the service should not let a
    ///     misbehaving agent take a process down, so this falls back instead of throwing.
    /// </remarks>
    private static LogEventRetentionOptions GetRetention(LogStreamInitialization initialization)
    {
        var maxAge = initialization.MaxAgeMinutes;
        var maxAgeUsable = maxAge > 0 && !double.IsNaN(maxAge) && maxAge <= TimeSpan.MaxValue.TotalMinutes;

        return new LogEventRetentionOptions
        {
            MaxEvents =
                initialization.MaxEvents > 0 ? initialization.MaxEvents : LogEventRetentionOptions.DefaultMaxEvents,
            MaxAgeMinutes = maxAgeUsable ? maxAge : LogEventRetentionOptions.DefaultMaxAgeMinutes,
        };
    }

    private LogStreamEvent[] Merge(IEnumerable<LogStreamEvent>? events)
    {
        if (events == null)
        {
            return [];
        }

        List<LogStreamEvent> added = [];
        foreach (var streamEvent in events)
        {
            if (streamEvent == null || !string.Equals(streamEvent.StreamId, _streamId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!_events.TryAdd(streamEvent.Sequence, streamEvent))
            {
                continue;
            }

            _highWatermark = Math.Max(_highWatermark, streamEvent.Sequence);
            added.Add(streamEvent);
        }

        return [.. added.OrderBy(streamEvent => streamEvent.Sequence)];
    }

    /// <summary>
    ///     Applies retention: by age first, then by count.
    /// </summary>
    /// <remarks>
    ///     Age is measured from the newest event held, NOT from this service's clock. The
    ///     timestamps are stamped by the agent, so comparing them against the service's wall clock
    ///     makes retention depend on the skew between two machines: a service a few minutes ahead
    ///     would age out every event as it arrived, and a late-attaching browser would be handed an
    ///     empty replay while a watching one saw events live. Measuring within the stream's own
    ///     time makes the window mean "the last N minutes of this process's logging", which is also
    ///     the more useful reading — a process that stopped logging an hour ago still shows what it
    ///     said before it stopped.
    /// </remarks>
    private void Prune()
    {
        if (_events.Count == 0)
        {
            return;
        }

        var newest = _events.Values.Max(streamEvent => streamEvent.TimestampUtc);
        var minimumTimestamp = newest - TimeSpan.FromMinutes(_retention.MaxAgeMinutes);
        foreach (
            var sequence in _events
                .Where(pair => pair.Value.TimestampUtc < minimumTimestamp)
                .Select(pair => pair.Key)
                .ToArray()
        )
        {
            _events.Remove(sequence);
        }

        var excess = _events.Count - _retention.MaxEvents;
        if (excess <= 0)
        {
            return;
        }

        foreach (var sequence in _events.Keys.Order().Take(excess).ToArray())
        {
            _events.Remove(sequence);
        }
    }
}
