using System.Reactive.Subjects;
using Diagnostic.Service.Common;
using Diagnostic.Service.Hubs;
using DiagnosticExplorer.Events;
using DiagnosticExplorer.Interface;
using DiagnosticExplorer.Util;
using Microsoft.AspNetCore.SignalR;

namespace Diagnostic.Service.ClientHandlers;

public sealed class DiagnosticClientHandler : HubProxyBase, IDiagnosticClient, IDisposable
{
    private readonly HubCallerContext _callerContext;
    private readonly IDiagnosticHubClient _client;
    private readonly Subject<SystemEvent[]> _eventsSetSubject = new();
    private readonly Subject<SystemEvent[]> _eventsStreamedSubject = new();
    private readonly ISubject<SystemEvent[]> _eventsSet;
    private readonly ISubject<SystemEvent[]> _eventsStreamed;
    private int _disposed;

    public DiagnosticClientHandler(
        HubCallerContext callerContext,
        IDiagnosticHubClient client,
        AsyncResultBucket responses
    )
        : base(responses)
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

    public async Task<DiagnosticResponse> GetDiagnostics(CancellationToken cancel)
    {
        var data = await SendRequest<byte[]>(cancel, requestId => _client.GetDiagnostics(requestId));
        return ProtobufUtil.Decompress<DiagnosticResponse>(data);
    }

    // Pass the caller's ConnectionAborted token (not CancellationToken.None): if the client
    // disconnects mid-request, the pending TaskCompletionSource in the shared response bucket is
    // released immediately rather than lingering until the round-trip timeout elapses.
    public Task<OperationResponse> SetProperty(string path, string? value)
    {
        return SendRequest<OperationResponse>(
            _callerContext.ConnectionAborted,
            requestId => _client.SetProperty(requestId, path, value)
        );
    }

    public Task<OperationResponse> ExecuteOperation(string path, string operation, string[] arguments)
    {
        return SendRequest<OperationResponse>(
            _callerContext.ConnectionAborted,
            requestId => _client.ExecuteOperation(requestId, path, operation, arguments)
        );
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
