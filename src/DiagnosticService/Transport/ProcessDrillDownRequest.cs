namespace Diagnostic.Service.Transport;

/// <summary>
///     A browser's drilldown request: which process, and which value inside it.
/// </summary>
/// <remarks>
///     Named apart from <see cref="DiagnosticExplorer.DrillDownRequest" />, which is the same
///     question asked of one agent and has no process to name. The service adds the process lookup
///     and forwards the rest.
/// </remarks>
public class ProcessDrillDownRequest
{
    public string Id { get; set; } = null!;
    public string[] ObjectPaths { get; set; } = [];
    public bool JsonHover { get; set; }
    public bool ExcludeEventViews { get; set; }
}
