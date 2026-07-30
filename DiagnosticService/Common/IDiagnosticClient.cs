using DiagnosticExplorer.Events;
using DiagnosticExplorer.Interface;

namespace Diagnostic.Service.Common;

public interface IDiagnosticClient
{
    IObservable<SystemEvent[]> EventsSet { get; }
    IObservable<SystemEvent[]> EventsStreamed { get; }
    Task<DiagnosticResponse> GetDiagnostics(CancellationToken cancel);
    Task<OperationResponse> SetProperty(string path, string? value);
    Task<OperationResponse> ExecuteOperation(string path, string operation, string[] arguments);
    Task SubscribeEvents();
    Task UnsubscribeEvents();
}
