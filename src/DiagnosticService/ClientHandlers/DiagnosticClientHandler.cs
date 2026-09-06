using System.Reactive.Subjects;
using Diagnostic.Service.Common;
using Diagnostic.Service.Hubs;
using DiagnosticExplorer;
using DiagnosticExplorer.Util;
using Microsoft.AspNetCore.SignalR;

namespace Diagnostic.Service.ClientHandlers;

public sealed class DiagnosticClientHandler : IDiagnosticClient, IDisposable
{
    /// <summary>The ceiling the deleted AsyncResultBucket applied to every request.</summary>
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(10);

    private readonly HubCallerContext _callerContext;
    private readonly IDiagnosticHubClient _client;
    private readonly Subject<SystemEvent[]> _eventsSetSubject = new();
    private readonly Subject<SystemEvent[]> _eventsStreamedSubject = new();
    private readonly ISubject<SystemEvent[]> _eventsSet;
    private readonly ISubject<SystemEvent[]> _eventsStreamed;
    private readonly TimeSpan _requestTimeout;
    private int _disposed;

    public DiagnosticClientHandler(
        HubCallerContext callerContext,
        IDiagnosticHubClient client,
        TimeSpan? requestTimeout = null
    )
    {
        _client = client;
        _callerContext = callerContext;
        _requestTimeout = requestTimeout ?? DefaultRequestTimeout;
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
    // What SignalR does NOT supply is the bucket's timeout, and the agent's connection is not the
    // caller's. Both gaps close by handing SignalR a real token in place of the
    // CancellationToken.None its typed proxy substitutes when the interface declares none: the
    // caller's own token, so a browser that gives up releases the pending invocation instead of
    // leaving it parked on an otherwise healthy agent, linked with a ceiling so that an agent
    // which never sends a completion at all still resolves.
    public Task<DiagnosticResponse> GetDiagnostics(CancellationToken cancel)
    {
        return Invoke(_client.GetDiagnostics, cancel);
    }

    public Task<OperationResponse> SetProperty(string path, string? value)
    {
        return Invoke(token => _client.SetProperty(path, value!, token), _callerContext.ConnectionAborted);
    }

    public Task<OperationResponse> ExecuteOperation(string path, string operation, string[] arguments)
    {
        return Invoke(
            token => _client.ExecuteOperation(path, operation, arguments, token),
            _callerContext.ConnectionAborted
        );
    }

    /// <summary>
    ///     Invokes a client result under the caller's token, bounded by the request timeout.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The linked source is disposed on every path, so a long-lived caller token does not
    ///         accumulate one registration per request.
    ///     </para>
    ///     <para>
    ///         A ceiling breach is reported as a TimeoutException rather than cancellation, which
    ///         is the distinction the AsyncResultBucket drew and the request loop still relies on:
    ///         it filters OperationCanceledException out of the error it pushes to the browser,
    ///         because a caller that gave up is not a fault. An agent that stopped answering IS
    ///         one, and reporting it as cancellation would leave the browser showing a healthy
    ///         panel of stale figures.
    ///     </para>
    /// </remarks>
    private async Task<T> Invoke<T>(Func<CancellationToken, Task<T>> invoke, CancellationToken cancel)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        linked.CancelAfter(_requestTimeout);
        try
        {
            return await invoke(linked.Token);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The agent did not answer within {_requestTimeout.TotalSeconds:F0}s on connection {ConnectionId}."
            );
        }
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
