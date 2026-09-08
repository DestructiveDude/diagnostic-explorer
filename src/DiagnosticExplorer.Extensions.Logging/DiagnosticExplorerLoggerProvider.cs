using System.Collections.Concurrent;
using DiagnosticExplorer.Logging;
using Microsoft.Extensions.Logging;

namespace DiagnosticExplorer.Extensions.Logging;

/// <summary>
///     A Microsoft.Extensions.Logging provider that publishes log events into a
///     <see cref="LogEventStore" />, so anything logged through <c>ILogger</c> appears on the live
///     event stream alongside the other logging-framework adapters.
/// </summary>
public sealed class DiagnosticExplorerLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentDictionary<string, DiagnosticExplorerLogger> _loggers = new(StringComparer.Ordinal);
    private IExternalScopeProvider? _scopeProvider;

    /// <param name="options">The routes that decide which categories and levels are published.</param>
    /// <param name="eventStore">
    ///     The store to publish into. Defaults to <see cref="DiagnosticManager.LogEventStore" />, the
    ///     process-wide stream.
    /// </param>
    public DiagnosticExplorerLoggerProvider(EventSinkRouteOptions options, LogEventStore? eventStore = null)
    {
        Router = new EventSinkRouter(options, eventStore);
    }

    public EventSinkRouter Router { get; }

    internal IExternalScopeProvider? ScopeProvider => _scopeProvider;

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(
            categoryName ?? string.Empty,
            category => new DiagnosticExplorerLogger(category, this)
        );
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    public void Dispose()
    {
        _loggers.Clear();
        Router.Dispose();
    }

    internal bool IsEnabled(string category, LogLevel logLevel)
    {
        return logLevel != LogLevel.None && Router.IsEnabled(category, logLevel);
    }
}
