using DiagnosticExplorer.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DiagnosticExplorer.Extensions.Logging;

/// <summary>
///     Registers <see cref="DiagnosticExplorerLoggerProvider" /> on an <see cref="ILoggingBuilder" />.
/// </summary>
public static class LoggingBuilderExtensions
{
    public static ILoggingBuilder AddDiagnosticExplorer(this ILoggingBuilder builder)
    {
        return builder.AddDiagnosticExplorer(DiagnosticManager.CurrentConfiguration.RuntimeOptions.Routing);
    }

    public static ILoggingBuilder AddDiagnosticExplorer(
        this ILoggingBuilder builder,
        EventSinkRouteOptions options,
        LogEventStore? eventStore = null
    )
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.Services.AddSingleton<ILoggerProvider>(_ => new DiagnosticExplorerLoggerProvider(options, eventStore));
        return builder;
    }

    public static ILoggingBuilder AddDiagnosticExplorer(
        this ILoggingBuilder builder,
        Action<EventSinkRouteOptions> configure,
        LogEventStore? eventStore = null
    )
    {
        if (configure == null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        EventSinkRouteOptions options = new();
        configure(options);
        return builder.AddDiagnosticExplorer(options, eventStore);
    }

    public static ILoggingBuilder AddDiagnosticExplorer(
        this ILoggingBuilder builder,
        IConfiguration configuration,
        LogEventStore? eventStore = null
    )
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        EventSinkRouteOptions options = configuration.Get<EventSinkRouteOptions>() ?? new EventSinkRouteOptions();
        return builder.AddDiagnosticExplorer(options, eventStore);
    }
}
