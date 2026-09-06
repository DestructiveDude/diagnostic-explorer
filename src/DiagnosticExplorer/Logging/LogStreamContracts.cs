#nullable enable annotations

using System.Runtime.Serialization;

namespace DiagnosticExplorer.Logging;

/// <summary>
///     One log event as it crosses the wire. Produced by <see cref="LogEventStore" />; consumed by
///     the diagnostics web client. Strings the producer always populates default to empty rather
///     than being nullable, so a deserialized instance is never half-initialised.
/// </summary>
[DataContract]
public sealed class LogStreamEvent
{
    [DataMember(Order = 1)]
    public string StreamId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public long Sequence { get; set; }

    [DataMember(Order = 3)]
    public DateTime TimestampUtc { get; set; }

    [DataMember(Order = 4)]
    public string LoggerCategory { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public int Level { get; set; }

    [DataMember(Order = 6)]
    public string? Message { get; set; }

    [DataMember(Order = 7)]
    public string? Detail { get; set; }

    [DataMember(Order = 8)]
    public int EventId { get; set; }

    [DataMember(Order = 9)]
    public string? EventName { get; set; }
}

/// <summary>How a route's logger-name pattern is compared against an event's category.</summary>
public enum LoggerNameMatchMode
{
    Exact,
    Prefix,
    Contains,
    Wildcard,
}

[DataContract]
public sealed class LogStreamRouteValue
{
    [DataMember(Order = 1)]
    public RouteValueSource Source { get; set; }

    /// <summary>Null when <see cref="Source" /> derives the value from the logger name.</summary>
    [DataMember(Order = 2)]
    public string? Value { get; set; }
}

[DataContract]
public sealed class LogStreamRouteDestination
{
    [DataMember(Order = 1)]
    public LogStreamRouteValue Category { get; set; } = new();

    [DataMember(Order = 2)]
    public LogStreamRouteValue Name { get; set; } = new();
}

[DataContract]
public sealed class LogStreamRoute
{
    [DataMember(Order = 1)]
    public int Order { get; set; }

    [DataMember(Order = 2)]
    public string LoggerName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public LoggerNameMatchMode LoggerNameMatchMode { get; set; }

    [DataMember(Order = 4)]
    public int? MinLevel { get; set; }

    [DataMember(Order = 5)]
    public int? MaxLevel { get; set; }

    [DataMember(Order = 6)]
    public bool StopProcessing { get; set; }

    [DataMember(Order = 7)]
    public List<LogStreamRouteDestination> Destinations { get; set; } = [];
}

[DataContract]
public sealed class LogStreamRoutingConfiguration
{
    [DataMember(Order = 1)]
    public EventSinkRouteMatchMode MatchMode { get; set; }

    [DataMember(Order = 2)]
    public List<LogStreamRoute> Routes { get; set; } = [];
}

/// <summary>
///     The snapshot a subscriber receives when it attaches: the routing in force, every retained
///     event, and the sequence number those events run up to, so the subscriber can tell a replayed
///     event from a live one.
/// </summary>
[DataContract]
public sealed class LogStreamInitialization
{
    [DataMember(Order = 1)]
    public string StreamId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public LogStreamRoutingConfiguration Routing { get; set; } = new();

    [DataMember(Order = 3)]
    public LogStreamEvent[] ReplayEvents { get; set; } = [];

    [DataMember(Order = 4)]
    public long HighWatermark { get; set; }

    [DataMember(Order = 5)]
    public int MaxEvents { get; set; }

    [DataMember(Order = 6)]
    public double MaxAgeMinutes { get; set; }
}
