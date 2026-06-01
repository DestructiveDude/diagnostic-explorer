using System;
using System.Threading;
using System.Threading.Tasks;

namespace DiagnosticExplorer;

public interface IDiagnosticClient
{
    Task<DiagnosticResponse> GetDiagnostics(CancellationToken cancel);
    Task<OperationResponse> SetProperty(string path, string? value);
    Task<OperationResponse> ExecuteOperation(string path, string operation, string[] arguments);
    Task SubscribeEvents();
    Task UnsubscribeEvents();

    IObservable<SystemEvent[]> EventsSet { get; }
    IObservable<SystemEvent[]> EventsStreamed { get; }

}