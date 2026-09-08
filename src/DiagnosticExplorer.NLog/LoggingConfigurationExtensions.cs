using DiagnosticExplorer.Logging;
using NLog.Config;

namespace DiagnosticExplorer.NLog;

/// <summary>
///     Registers <see cref="DiagnosticExplorerTarget" /> on an NLog <see cref="LoggingConfiguration" />.
/// </summary>
public static class LoggingConfigurationExtensions
{
    public static LoggingConfiguration AddDiagnosticExplorer(this LoggingConfiguration configuration)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        DiagnosticExplorerTarget target = configuration.AddDiagnosticExplorer(
            "DiagnosticExplorer",
            DiagnosticManager.CurrentConfiguration.RuntimeOptions.Routing
        );
        configuration.AddRuleForAllLevels(target);
        return configuration;
    }

    public static DiagnosticExplorerTarget AddDiagnosticExplorer(
        this LoggingConfiguration configuration,
        string targetName,
        EventSinkRouteOptions options
    )
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (string.IsNullOrWhiteSpace(targetName))
        {
            throw new ArgumentException("A target name is required.", nameof(targetName));
        }

        DiagnosticExplorerTarget target = new(options);
        configuration.AddTarget(targetName, target);
        return target;
    }
}
