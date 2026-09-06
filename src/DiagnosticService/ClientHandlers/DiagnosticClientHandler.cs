using System.Reactive.Subjects;
using Diagnostic.Service.Common;
using Diagnostic.Service.Hubs;
using DiagnosticExplorer;
using DiagnosticExplorer.Util;
using Microsoft.AspNetCore.SignalR;

namespace Diagnostic.Service.ClientHandlers;

public sealed class DiagnosticClientHandler : IDiagnosticClient, IDisposable
{
    private readonly HubCallerContext _callerContext;
    private readonly IDiagnosticHubClient _client;
    private readonly Subject<SystemEvent[]> _eventsSetSubject = new();
    private readonly Subject<SystemEvent[]> _eventsStreamedSubject = new();
    private readonly ISubject<SystemEvent[]> _eventsSet;
    private readonly ISubject<SystemEvent[]> _eventsStreamed;
    private int _disposed;

    public DiagnosticClientHandler(HubCallerContext callerContext, IDiagnosticHubClient client)
    {
        _client = client;
        _callerContext = callerContext;
        _eventsSet = Subject.Synchronize(_eventsSetSubject);
        _eventsStreamed = Subject.Synchronize(_eventsStreamedSubject);
        ConnectionId = callerContext.ConnectionId;
    }

    public string ConnectionId { get; }
    public IObservable<SystemEvent[]> EventsSet => _eventsSet;
    public IObservable<SystemEvent[]> EventsStreamed => _eventsStreamed;

    // These three are SignalR client results. The request id, the shared response bucket and the
    // ConnectionAborted registration that used to release a pending call on disconnect are all
    // gone: SignalR correlates the invocation itself and faults it when the connection ends, which
    // is exactly what the hand-rolled machinery existed to do. The cancel parameter is kept on
    // GetDiagnostics for its callers' benefit and observed before the call.
    public async Task<DiagnosticResponse> GetDiagnostics(CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        return await _client.GetDiagnostics();
    }

    public Task<OperationResponse> SetProperty(string path, string? value)
    {
        return _client.SetProperty(path, value!);
    }

    public Task<OperationResponse> ExecuteOperation(string path, string operation, string[] arguments)
    {
        return _client.ExecuteOperation(path, operation, arguments);
    }

    public async Task SubscribeEvents()
    {
        await _client.SubscribeEvents();
    }

    public async Task UnsubscribeEvents()
    {
        await _client.UnsubscribeEvents();
    }

    public event EventHandler? Disconnected;

    public void Arm()
    {
        _callerContext.ConnectionAborted.Register(() => Disconnected?.Invoke(this, EventArgs.Empty));
    }

    // SetEvents/StreamEvents can be invoked concurrently for a single client under
    // MaximumParallelInvocationsPerClient; the _eventsSet/_eventsStreamed subjects are wrapped in
    // Subject.Synchronize (see field declarations) so their OnNext is already serialized. (A6)
    public void SetEvents(SystemEvent[] events)
    {
        _eventsSet.OnNext(events);
    }

    public void StreamEvents(SystemEvent[] evt)
    {
        _eventsStreamed.OnNext(evt);
    }

    public void CloseConnection()
    {
        _callerContext.Abort();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _eventsSetSubject.Dispose();
        _eventsStreamedSubject.Dispose();
    }
}
