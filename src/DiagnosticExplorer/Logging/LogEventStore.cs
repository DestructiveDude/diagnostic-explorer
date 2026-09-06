#nullable enable annotations

using System.Threading.Channels;

namespace DiagnosticExplorer.Logging;

/// <summary>
///     Holds a bounded, time-limited window of log events for one stream, and fans new ones out to
///     live subscribers. A subscriber that attaches receives every retained event plus the sequence
///     number they run to, so it can distinguish replay from live traffic.
/// </summary>
/// <remarks>
///     The clock is injected rather than read from <c>DateTime.UtcNow</c> so retention is testable
///     without sleeping: pruning is entirely a function of the timestamps this store stamps.
/// </remarks>
public sealed class LogEventStore
{
    private const int DefaultLiveSubscriptionCapacity = 1024;
    private readonly object _sync = new();
    private readonly List<LogStreamEvent> _events = [];
    private readonly HashSet<LogEventStoreSubscription> _subscriptions = [];
    private readonly TimeProvider _timeProvider;
    private LogEventRetentionOptions _retention;
    private LogStreamRoutingConfiguration _routing;
    private long _sequence;

    public LogEventStore(
        LogEventRetentionOptions? retention = null,
        string? streamId = null,
        TimeProvider? timeProvider = null
    )
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _retention = (retention ?? new LogEventRetentionOptions()).CloneAndValidate();
        StreamId = string.IsNullOrWhiteSpace(streamId) ? Guid.NewGuid().ToString("N") : streamId;
        _routing = new LogStreamRoutingConfiguration();
    }

    public string StreamId { get; }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    public void Configure(LogEventRetentionOptions retention, LogStreamRoutingConfiguration routing)
    {
        if (retention == null)
        {
            throw new ArgumentNullException(nameof(retention));
        }

        if (routing == null)
        {
            throw new ArgumentNullException(nameof(routing));
        }

        lock (_sync)
        {
            _retention = retention.CloneAndValidate();
            _routing = routing.Clone();
            Prune(UtcNow);
        }
    }

    public void ConfigureRouting(LogStreamRoutingConfiguration routing)
    {
        if (routing == null)
        {
            throw new ArgumentNullException(nameof(routing));
        }

        lock (_sync)
        {
            _routing = routing.Clone();
        }
    }

    /// <summary>Records an event and pushes it to every live subscriber. Returns its sequence number.</summary>
    public long Publish(EventSinkLogEvent logEvent)
    {
        if (logEvent == null)
        {
            throw new ArgumentNullException(nameof(logEvent));
        }

        lock (_sync)
        {
            DateTime timestampUtc = UtcNow;
            Prune(timestampUtc);

            LogStreamEvent streamEvent = new()
            {
                StreamId = StreamId,
                Sequence = ++_sequence,
                TimestampUtc = timestampUtc,
                LoggerCategory = logEvent.Category,
                Level = (int)logEvent.Level,
                Message = logEvent.Message,
                Detail = logEvent.Detail,
                EventId = logEvent.EventId.Id,
                EventName = logEvent.EventId.Name,
            };

            _events.Add(streamEvent);
            Prune(timestampUtc);

            // A subscriber whose bounded channel is full is dropped rather than allowed to stall the
            // publishing thread, which is the caller's logging path. ToArray so the set can be
            // mutated while iterating.
            foreach (LogEventStoreSubscription subscription in _subscriptions.ToArray())
            {
                if (subscription.TryWrite(streamEvent))
                {
                    continue;
                }

                _ = _subscriptions.Remove(subscription);
                subscription.Complete();
            }

            return streamEvent.Sequence;
        }
    }

    public LogEventStoreSubscription CreateSubscription(int liveSubscriptionCapacity = DefaultLiveSubscriptionCapacity)
    {
        if (liveSubscriptionCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(liveSubscriptionCapacity));
        }

        lock (_sync)
        {
            Prune(UtcNow);

            LogEventStoreSubscription subscription = new(this, liveSubscriptionCapacity);
            _ = _subscriptions.Add(subscription);
            subscription.SetInitialization(CreateInitializationLocked());
            return subscription;
        }
    }

    public LogStreamInitialization CreateInitialization()
    {
        lock (_sync)
        {
            Prune(UtcNow);
            return CreateInitializationLocked();
        }
    }

    private void RemoveSubscription(LogEventStoreSubscription subscription)
    {
        lock (_sync)
        {
            _ = _subscriptions.Remove(subscription);
        }
    }

    private LogStreamInitialization CreateInitializationLocked()
    {
        return new LogStreamInitialization
        {
            StreamId = StreamId,
            Routing = _routing.Clone(),
            ReplayEvents = [.. _events],
            HighWatermark = _sequence,
            MaxEvents = _retention.MaxEvents,
            MaxAgeMinutes = _retention.MaxAgeMinutes,
        };
    }

    /// <summary>Drops events past the age limit, then past the count limit. Caller holds <see cref="_sync" />.</summary>
    private void Prune(DateTime timestampUtc)
    {
        DateTime minimumTimestamp = timestampUtc - TimeSpan.FromMinutes(_retention.MaxAgeMinutes);
        int firstCurrentIndex = _events.FindIndex(streamEvent => streamEvent.TimestampUtc >= minimumTimestamp);
        if (firstCurrentIndex < 0)
        {
            _events.Clear();
        }
        else if (firstCurrentIndex > 0)
        {
            _events.RemoveRange(0, firstCurrentIndex);
        }

        int excess = _events.Count - _retention.MaxEvents;
        if (excess > 0)
        {
            _events.RemoveRange(0, excess);
        }
    }

    public sealed class LogEventStoreSubscription : IDisposable
    {
        private readonly LogEventStore _owner;
        private readonly Channel<LogStreamEvent> _channel;
        private bool _disposed;

        internal LogEventStoreSubscription(LogEventStore owner, int capacity)
        {
            _owner = owner;
            _channel = Channel.CreateBounded<LogStreamEvent>(
                new BoundedChannelOptions(capacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                }
            );
        }

        public LogStreamInitialization? Initialization { get; private set; }

        public ChannelReader<LogStreamEvent> Events => _channel.Reader;

        internal bool TryWrite(LogStreamEvent streamEvent)
        {
            return _channel.Writer.TryWrite(streamEvent);
        }

        internal void SetInitialization(LogStreamInitialization initialization)
        {
            Initialization = initialization;
        }

        internal void Complete()
        {
            _ = _channel.Writer.TryComplete();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.RemoveSubscription(this);
            Complete();
        }
    }
}

public static class LogStreamRoutingConfigurationExtensions
{
    /// <summary>Projects the adapter-facing route options into the wire shape sent to clients.</summary>
    public static LogStreamRoutingConfiguration CreateSnapshot(this EventSinkRouteOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return new LogStreamRoutingConfiguration
        {
            MatchMode = options.MatchMode,
            Routes =
            [
                .. (options.Routes ?? []).Select(
                    (route, index) =>
                        new LogStreamRoute
                        {
                            Order = index,
                            LoggerName = route.CategoryPattern ?? string.Empty,
                            LoggerNameMatchMode =
                                route.CategoryPattern == "*"
                                    ? LoggerNameMatchMode.Wildcard
                                    : LoggerNameMatchMode.Prefix,
                            MinLevel = route.MinLevel.HasValue ? (int)route.MinLevel.Value : null,
                            MaxLevel = route.MaxLevel.HasValue ? (int)route.MaxLevel.Value : null,
                            StopProcessing = route.StopProcessing,
                            Destinations =
                            [
                                .. route.Destinations.Select(destination => new LogStreamRouteDestination
                                {
                                    Category = destination.SinkCategory.ToSnapshot(),
                                    Name = destination.SinkName.ToSnapshot(),
                                }),
                            ],
                        }
                ),
            ],
        };
    }

    public static LogStreamRoutingConfiguration Clone(this LogStreamRoutingConfiguration routing)
    {
        if (routing == null)
        {
            throw new ArgumentNullException(nameof(routing));
        }

        return new LogStreamRoutingConfiguration
        {
            MatchMode = routing.MatchMode,
            Routes =
            [
                .. (routing.Routes ?? []).Select(route => new LogStreamRoute
                {
                    Order = route.Order,
                    LoggerName = route.LoggerName,
                    LoggerNameMatchMode = route.LoggerNameMatchMode,
                    MinLevel = route.MinLevel,
                    MaxLevel = route.MaxLevel,
                    StopProcessing = route.StopProcessing,
                    Destinations =
                    [
                        .. route.Destinations.Select(destination => new LogStreamRouteDestination
                        {
                            Category = new LogStreamRouteValue
                            {
                                Source = destination.Category.Source,
                                Value = destination.Category.Value,
                            },
                            Name = new LogStreamRouteValue
                            {
                                Source = destination.Name.Source,
                                Value = destination.Name.Value,
                            },
                        }),
                    ],
                }),
            ],
        };
    }

    /// <summary>
    ///     Destinations are validated non-null when a route is compiled, but this is also reachable
    ///     from a hand-built options object, so an absent value snapshots as an empty fixed value
    ///     rather than throwing during projection.
    /// </summary>
    private static LogStreamRouteValue ToSnapshot(this RouteValue? routeValue)
    {
        return routeValue == null
            ? new LogStreamRouteValue()
            : new LogStreamRouteValue { Source = routeValue.Source, Value = routeValue.Value };
    }
}
