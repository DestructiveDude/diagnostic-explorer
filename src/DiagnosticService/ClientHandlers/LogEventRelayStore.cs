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
internal sealed class LogEventRelayStore(TimeProvider timeProvider)
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

    private static LogEventRetentionOptions GetRetention(LogStreamInitialization initialization)
    {
        // A zero means the agent did not state a limit, not that it wants to retain nothing.
        return new LogEventRetentionOptions
        {
            MaxEvents =
                initialization.MaxEvents > 0 ? initialization.MaxEvents : LogEventRetentionOptions.DefaultMaxEvents,
            MaxAgeMinutes =
                initialization.MaxAgeMinutes > 0
                    ? initialization.MaxAgeMinutes
                    : LogEventRetentionOptions.DefaultMaxAgeMinutes,
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

    private void Prune()
    {
        var minimumTimestamp = timeProvider.GetUtcNow().UtcDateTime - TimeSpan.FromMinutes(_retention.MaxAgeMinutes);
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
