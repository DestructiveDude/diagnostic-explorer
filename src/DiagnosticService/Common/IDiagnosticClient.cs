using DiagnosticExplorer;
using DiagnosticExplorer.Logging;

namespace Diagnostic.Service.Common;

public interface IDiagnosticClient
{
    /// <summary>The snapshot an agent sends when its log stream starts, or restarts.</summary>
    IObservable<LogStreamInitialization> LogStreamInitialized { get; }

    /// <summary>Live log events, batched by the agent.</summary>
    IObservable<LogStreamEvent[]> LogStreamEvents { get; }
    Task<DiagnosticResponse> GetDiagnostics(CancellationToken cancel);
    Task<DrillDownResponse> GetDrillDown(DrillDownRequest request);

    /// <param name="objectPaths">
    ///     The drilldown the action was triggered from, empty for the main view.
    /// </param>
    Task<OperationResponse> SetProperty(string[] objectPaths, string path, string? value);
    Task<OperationResponse> ExecuteOperation(string[] objectPaths, string path, string operation, string[] arguments);
    Task SubscribeEvents();
    Task UnsubscribeEvents();
}
