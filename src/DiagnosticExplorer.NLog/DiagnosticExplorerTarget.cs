using System.Text;
using DiagnosticExplorer.Logging;
using NLog;
using NLog.Layouts;
using NLog.Targets;
using MicrosoftLogLevel = Microsoft.Extensions.Logging.LogLevel;
using NLogLevel = NLog.LogLevel;

namespace DiagnosticExplorer.NLog;

/// <summary>
///     An NLog target that publishes log events into a <see cref="LogEventStore" /> through an
///     <see cref="EventSinkRouter" />, so an NLog host feeds the same live event stream the other
///     logging-framework adapters do.
/// </summary>
[Target("DiagnosticExplorer")]
public sealed class DiagnosticExplorerTarget : TargetWithLayout
{
    private readonly LogEventStore _eventStore;
    private EventSinkRouter? _router;

    public DiagnosticExplorerTarget()
        : this(new EventSinkRouteOptions()) { }

    /// <param name="options">The routes that decide which categories and levels are published.</param>
    /// <param name="eventStore">
    ///     The store to publish into. Defaults to <see cref="DiagnosticManager.LogEventStore" />, the
    ///     process-wide stream.
    /// </param>
    public DiagnosticExplorerTarget(EventSinkRouteOptions options, LogEventStore? eventStore = null)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _eventStore = eventStore ?? DiagnosticManager.LogEventStore;
        Layout = new SimpleLayout("${message}");
    }

    public EventSinkRouteOptions Options { get; set; }

    public string FallbackCategory { get; set; } = "Application";

    protected override void InitializeTarget()
    {
        base.InitializeTarget();
        if (_router == null)
        {
            _router = new EventSinkRouter(Options, _eventStore);
        }
        else
        {
            _router.Reconfigure(Options);
        }
    }

    protected override void CloseTarget()
    {
        _router?.Dispose();
        _router = null;
        base.CloseTarget();
    }

    protected override void Write(LogEventInfo logEvent)
    {
        if (logEvent == null)
        {
            throw new ArgumentNullException(nameof(logEvent));
        }

        EventSinkRouter? router = _router;
        if (router == null)
        {
            return;
        }
        string category = string.IsNullOrWhiteSpace(logEvent.LoggerName) ? FallbackCategory : logEvent.LoggerName!;
        MicrosoftLogLevel level = ToLogLevel(logEvent.Level);
        if (!router.IsEnabled(category, level))
        {
            return;
        }

        string renderedMessage = RenderLogEvent(Layout, logEvent);
        router.Route(
            new EventSinkLogEvent(
                category,
                level,
                GetHeadline(renderedMessage),
                CreateDetail(logEvent, renderedMessage)
            )
        );
    }

    private static MicrosoftLogLevel ToLogLevel(NLogLevel level)
    {
        if (level == NLogLevel.Trace)
        {
            return MicrosoftLogLevel.Trace;
        }

        if (level == NLogLevel.Debug)
        {
            return MicrosoftLogLevel.Debug;
        }

        if (level == NLogLevel.Info)
        {
            return MicrosoftLogLevel.Information;
        }

        if (level == NLogLevel.Warn)
        {
            return MicrosoftLogLevel.Warning;
        }

        if (level == NLogLevel.Error)
        {
            return MicrosoftLogLevel.Error;
        }

        if (level == NLogLevel.Fatal)
        {
            return MicrosoftLogLevel.Critical;
        }

        throw new ArgumentOutOfRangeException(nameof(level));
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

    private static string? CreateDetail(LogEventInfo logEvent, string? renderedMessage)
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

        // Iterated through the generic interface NLog declares. Upstream pattern-matches against
        // the non-generic IDictionary instead, which NLog's PropertiesDictionary does not satisfy,
        // so every event property was silently dropped. HasProperties keeps the lazily-created
        // dictionary from being allocated for the common case of an event carrying none.
        if (logEvent.HasProperties)
        {
            foreach (KeyValuePair<object, object> property in logEvent.Properties)
            {
                detail.Append("Property.").Append(property.Key).Append(": ").AppendLine(property.Value?.ToString());
            }
        }

        return detail.Length == 0 ? null : detail.ToString().TrimEnd();
    }
}
