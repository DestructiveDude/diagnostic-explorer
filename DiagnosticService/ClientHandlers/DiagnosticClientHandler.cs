using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using DiagnosticExplorer;
using DiagnosticExplorer.Util;
using Diagnostics.Service.Common.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace DiagWebService.ClientHandlers;

public class DiagnosticClientHandler : HubProxyBase, IDiagnosticClient
{
    private readonly IDiagnosticHubClient _client;
    private readonly HubCallerContext _callerContext;
    private readonly ISubject<SystemEvent[]> _eventsSet = Subject.Synchronize(new Subject<SystemEvent[]>());
    private readonly ISubject<SystemEvent[]> _eventsStreamed = Subject.Synchronize(new Subject<SystemEvent[]>());
    public event EventHandler? Disconnected;
    public IObservable<SystemEvent[]> EventsSet => _eventsSet;
    public IObservable<SystemEvent[]> EventsStreamed => _eventsStreamed;

    public DiagnosticClientHandler(HubCallerContext callerContext, IDiagnosticHubClient client, AsyncResultBucket responses)
        : base(responses)
    {
        _client = client;
        _callerContext = callerContext;
        ConnectionId = callerContext.ConnectionId;
        _callerContext.ConnectionAborted.Register(() => Disconnected?.Invoke(this, EventArgs.Empty));
    }

    public string ConnectionId { get; }

    public async Task<DiagnosticResponse> GetDiagnostics(CancellationToken cancel)
    {
        byte[] data = await SendRequest<byte[]>(cancel, requestId => _client.GetDiagnostics(requestId));
        return ProtobufUtil.Decompress<DiagnosticResponse>(data);
    }

    // Pass the caller's ConnectionAborted token (not CancellationToken.None): if the client
    // disconnects mid-request, the pending TaskCompletionSource in the shared response bucket is
    // released immediately rather than lingering until the round-trip timeout elapses.
    public Task<OperationResponse> SetProperty(string path, string? value)
    {
        return SendRequest<OperationResponse>(_callerContext.ConnectionAborted, requestId => _client.SetProperty(requestId, path, value));
    }

    public Task<OperationResponse> ExecuteOperation(string path, string operation, string[] arguments)
    {
        return SendRequest<OperationResponse>(_callerContext.ConnectionAborted, requestId => _client.ExecuteOperation(requestId, path, operation, arguments));
    }

    public async Task SubscribeEvents()
    {
        await _client.SubscribeEvents();
    }

    public async Task UnsubscribeEvents()
    {
        await _client.UnsubscribeEvents();
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
}
