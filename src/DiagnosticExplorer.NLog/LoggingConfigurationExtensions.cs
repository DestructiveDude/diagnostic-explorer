using DiagnosticExplorer.Logging;
using NLog.Config;

namespace DiagnosticExplorer.NLog;

/// <summary>
///     Registers <see cref="DiagnosticExplorerTarget" /> on an NLog <see cref="LoggingConfiguration" />.
/// </summary>
/// <remarks>
///     Upstream also carries a one-argument overload that reads the routes from
///     <c>DiagnosticManager.CurrentConfiguration</c>. That configuration surface does not exist here
///     yet, so the routes have to be supplied explicitly for now.
/// </remarks>
public static class LoggingConfigurationExtensions
{
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
