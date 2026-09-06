using DiagnosticExplorer.Logging;
using Microsoft.Extensions.Logging;

namespace DiagnosticExplorer.Extensions.Logging;

/// <summary>
///     The per-category <see cref="ILogger" /> handed out by
///     <see cref="DiagnosticExplorerLoggerProvider" />. Every log call is offered to the provider's
///     <see cref="EventSinkRouter" />, which decides whether it reaches the store.
/// </summary>
internal sealed class DiagnosticExplorerLogger : ILogger
{
    private readonly string _category;
    private readonly DiagnosticExplorerLoggerProvider _provider;

    public DiagnosticExplorerLogger(string category, DiagnosticExplorerLoggerProvider provider)
    {
        _category = category;
        _provider = provider;
    }

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        return _provider.ScopeProvider?.Push(state) ?? NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return _provider.IsEnabled(_category, logLevel);
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        string? renderedMessage = formatter is null ? state?.ToString() : formatter(state, exception);
        string? detail = DiagnosticExplorerLogDetail.Create(
            renderedMessage,
            exception,
            eventId,
            state,
            _provider.ScopeProvider
        );
        _provider.Router.Route(
            new EventSinkLogEvent(
                _category,
                logLevel,
                DiagnosticExplorerLogDetail.GetHeadline(renderedMessage),
                detail,
                eventId
            )
        );
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose() { }
    }
}
