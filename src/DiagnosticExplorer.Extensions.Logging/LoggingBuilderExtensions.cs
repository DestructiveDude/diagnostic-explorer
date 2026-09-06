using DiagnosticExplorer.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DiagnosticExplorer.Extensions.Logging;

/// <summary>
///     Registers <see cref="DiagnosticExplorerLoggerProvider" /> on an <see cref="ILoggingBuilder" />.
/// </summary>
/// <remarks>
///     Upstream also carries a parameterless overload that reads the routes from
///     <c>DiagnosticManager.CurrentConfiguration</c>. That configuration surface does not exist here
///     yet, so the routes have to be supplied explicitly for now. The <c>eventStore</c> parameter is
///     likewise ours: upstream always publishes into the process-wide stream, and a host — or a
///     test — sometimes wants a stream of its own.
/// </remarks>
public static class LoggingBuilderExtensions
{
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

        builder.AddProvider(new DiagnosticExplorerLoggerProvider(options, eventStore));
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
