#nullable enable annotations

using Microsoft.Extensions.Logging;

namespace DiagnosticExplorer.Logging;

/// <summary>
///     A log event handed to <see cref="EventSinkRouter" /> by a logging-framework adapter, before
///     routing decides whether it reaches the store. Immutable: the adapters that create these run
///     on the caller's logging thread.
/// </summary>
public sealed class EventSinkLogEvent
{
    public EventSinkLogEvent(
        string? category,
        LogLevel level,
        string? message,
        string? detail = null,
        EventId eventId = default,
        IReadOnlyDictionary<string, object>? properties = null
    )
    {
        Category = category ?? string.Empty;
        Level = level;
        Message = message;
        Detail = detail;
        EventId = eventId;
        Properties = properties;
    }

    public string Category { get; }

    public LogLevel Level { get; }

    public string? Message { get; }

    public string? Detail { get; }

    public EventId EventId { get; }

    public IReadOnlyDictionary<string, object>? Properties { get; }
}
