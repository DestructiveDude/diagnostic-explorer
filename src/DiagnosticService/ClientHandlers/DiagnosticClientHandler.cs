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

    // These three are SignalR client results. The request id and the shared response bucket are
    // gone: SignalR correlates the invocation itself and faults it when the AGENT's connection
    // ends.
    //
    // That is not the same connection as the caller's. A browser that disconnects mid-request
    // would otherwise leave this await parked until the agent answers or its own connection drops,
    // so the caller's token is still raced against the invocation below — which is what
    // ConnectionAborted did before.
    public Task<DiagnosticResponse> GetDiagnostics(CancellationToken cancel)
    {
        return AwaitOrAbandon(_client.GetDiagnostics(), cancel);
    }

    public Task<OperationResponse> SetProperty(string path, string? value)
    {
        return AwaitOrAbandon(_client.SetProperty(path, value!), _callerContext.ConnectionAborted);
    }

    public Task<OperationResponse> ExecuteOperation(string path, string operation, string[] arguments)
    {
        return AwaitOrAbandon(_client.ExecuteOperation(path, operation, arguments), _callerContext.ConnectionAborted);
    }

    /// <summary>
    ///     Waits for a client result, giving up as soon as the caller cancels.
    /// </summary>
    /// <remarks>
    ///     Abandons the wait, not the work: a client result cannot be cancelled mid-flight, so the
    ///     agent still completes its invocation and SignalR still resolves it. The point is that
    ///     the caller's request thread is released at once rather than held until the agent
    ///     answers. The registration is disposed on every path, cancelled or not, so a long-lived
    ///     caller token does not accumulate one per request.
    /// </remarks>
    private static async Task<T> AwaitOrAbandon<T>(Task<T> invocation, CancellationToken cancel)
    {
        if (!cancel.CanBeCanceled)
        {
            return await invocation;
        }

        TaskCompletionSource<bool> cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancel.Register(() => cancelled.TrySetResult(true)))
        {
            if (await Task.WhenAny(invocation, cancelled.Task) != (Task)invocation)
            {
                cancel.ThrowIfCancellationRequested();
            }
        }

        return await invocation;
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
