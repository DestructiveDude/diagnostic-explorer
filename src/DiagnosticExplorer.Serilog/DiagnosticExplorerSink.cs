using System.Text;
using DiagnosticExplorer.Logging;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace DiagnosticExplorer.Serilog;

/// <summary>
///     A Serilog sink that publishes log events into a <see cref="LogEventStore" /> through an
///     <see cref="EventSinkRouter" />, so a Serilog host feeds the same live event stream the other
///     logging-framework adapters do.
/// </summary>
public sealed class DiagnosticExplorerSink : ILogEventSink
{
    private const string SourceContextProperty = "SourceContext";
    private readonly string _fallbackCategory;

    /// <param name="options">The routes that decide which categories and levels are published.</param>
    /// <param name="fallbackCategory">
    ///     The category used when an event carries no <c>SourceContext</c> property.
    /// </param>
    /// <param name="eventStore">
    ///     The store to publish into. Defaults to <see cref="DiagnosticManager.LogEventStore" />, the
    ///     process-wide stream.
    /// </param>
    public DiagnosticExplorerSink(
        EventSinkRouteOptions options,
        string fallbackCategory = "Application",
        LogEventStore? eventStore = null
    )
    {
        if (string.IsNullOrWhiteSpace(fallbackCategory))
        {
            throw new ArgumentException("A fallback category is required.", nameof(fallbackCategory));
        }

        _fallbackCategory = fallbackCategory;
        Router = new EventSinkRouter(options, eventStore);
    }

    public EventSinkRouter Router { get; }

    public void Emit(LogEvent logEvent)
    {
        if (logEvent == null)
        {
            throw new ArgumentNullException(nameof(logEvent));
        }

        string category = GetCategory(logEvent);
        LogLevel level = ToLogLevel(logEvent.Level);
        if (!Router.IsEnabled(category, level))
        {
            return;
        }

        string renderedMessage = logEvent.RenderMessage();
        Router.Route(
            new EventSinkLogEvent(
                category,
                level,
                GetHeadline(renderedMessage),
                CreateDetail(logEvent, renderedMessage)
            )
        );
    }

    private static LogLevel ToLogLevel(LogEventLevel level)
    {
        return level switch
        {
            LogEventLevel.Verbose => LogLevel.Trace,
            LogEventLevel.Debug => LogLevel.Debug,
            LogEventLevel.Information => LogLevel.Information,
            LogEventLevel.Warning => LogLevel.Warning,
            LogEventLevel.Error => LogLevel.Error,
            LogEventLevel.Fatal => LogLevel.Critical,
            _ => throw new ArgumentOutOfRangeException(nameof(level)),
        };
    }

    private static string? GetHeadline(string? message)
    {
        // Written as an explicit null test rather than string.IsNullOrEmpty because only net10.0
        // carries the [NotNullWhen] annotation that narrows the latter; net48 does not.
        if (message is null || message.Length == 0)
        {
            return message;
        }

        int newLine = message.IndexOfAny(['\r', '\n']);
        return newLine < 0 ? message : message.Substring(0, newLine);
    }

    private static string? CreateDetail(LogEvent logEvent, string? renderedMessage)
    {
        StringBuilder detail = new();
        string? headline = GetHeadline(renderedMessage);
        if (renderedMessage is not null && headline is not null && renderedMessage.Length != headline.Length)
        {
            detail.AppendLine(renderedMessage);
        }

        if (logEvent.Exception != null)
        {
            detail.AppendLine(logEvent.Exception.ToString());
        }

        foreach (KeyValuePair<string, LogEventPropertyValue> property in logEvent.Properties)
        {
            if (property.Key == SourceContextProperty)
            {
                continue;
            }

            detail.Append("Property.").Append(property.Key).Append(": ").AppendLine(property.Value.ToString());
        }

        return detail.Length == 0 ? null : detail.ToString().TrimEnd();
    }

    private string GetCategory(LogEvent logEvent)
    {
        if (
            logEvent.Properties.TryGetValue(SourceContextProperty, out LogEventPropertyValue? value)
            && value is ScalarValue scalar
            && scalar.Value is string sourceContext
            && !string.IsNullOrWhiteSpace(sourceContext)
        )
        {
            return sourceContext;
        }

        return _fallbackCategory;
    }
}
