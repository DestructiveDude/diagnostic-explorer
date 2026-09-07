#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using DiagnosticExplorer.Logging;
using DiagnosticExplorer.Util;
using log4net;
using Microsoft.AspNetCore.SignalR.Client;

namespace DiagnosticExplorer;

internal sealed class HubServerAdapter : IDiagnosticHubClient, IDisposable
{
    private static readonly ILog _log = LogManager.GetLogger(typeof(HubServerAdapter));

    // _eventLock serializes subscribe/unsubscribe so a re-subscribe can't orphan the prior
    // CancellationTokenSource and its still-running SendEventStream loop.
    private readonly object _eventLock = new();

    /// <summary>How many log events one StreamLogEvents frame may carry.</summary>
    private const int MaxEventsPerFrame = 100;

    // _requestGate serializes the three client results against each other. See Run.
    //
    // Deliberately never disposed. RegistrationHandler.DisposeConnection disposes the adapter
    // while the connection is still up, so an invocation that arrived a moment earlier can still
    // be inside Run; disposing the semaphore under it would make Release throw
    // ObjectDisposedException from the finally and replace whatever the request actually returned.
    // SemaphoreSlim only needs disposing once AvailableWaitHandle has been touched, which nothing
    // here does, so not disposing it removes the race rather than catching it.
    [SuppressMessage(
        "Design",
        "CA2213:Disposable fields should be disposed",
        Justification = "See the comment above: disposing it would introduce a teardown race, and "
            + "SemaphoreSlim allocates nothing to dispose unless AvailableWaitHandle is used."
    )]
    private readonly SemaphoreSlim _requestGate = new(1, 1);

    private readonly HubConnection _hubConn;
    private readonly LogEventStore? _eventStore;
    private CancellationTokenSource? _writeEventCancel;
    private Task? _writeEventTask;

    /// <summary>
    ///     The store this adapter streams. Defaults to the process-wide one.
    /// </summary>
    /// <remarks>
    ///     Resolved on use rather than captured, so the default follows DiagnosticManager rather
    ///     than freezing whatever it held when this adapter was built. The parameter exists so a
    ///     test can stream its own store instead of publishing into the process-wide singleton,
    ///     which is the convention the rest of this assembly's tests already follow.
    /// </remarks>
    private LogEventStore EventStore => _eventStore ?? DiagnosticManager.LogEventStore;

    public HubServerAdapter(HubConnection hubConn, LogEventStore? eventStore = null)
    {
        _hubConn = hubConn;
        _eventStore = eventStore;

        // Registered through the value-returning On overloads, which is what makes these client
        // results rather than one-way notifications.
        //
        // CancellationToken.None is not a shortcut, it is the only value available: the service's
        // typed proxy strips the trailing token from the wire arguments (TypedClientBuilder), so
        // an agent never learns that its caller gave up. The token on IDiagnosticHubClient bounds
        // the SERVICE's wait; the agent always runs the work to completion.
        _hubConn.On<DiagnosticResponse>(
            nameof(IDiagnosticHubClient.GetDiagnostics),
            () => GetDiagnostics(CancellationToken.None)
        );

        _hubConn.On<string, string, OperationResponse>(
            nameof(IDiagnosticHubClient.SetProperty),
            (path, value) => SetProperty(path, value, CancellationToken.None)
        );

        _hubConn.On<string, string, string[], OperationResponse>(
            nameof(IDiagnosticHubClient.ExecuteOperation),
            (path, operation, args) => ExecuteOperation(path, operation, args, CancellationToken.None)
        );

        _hubConn.On(nameof(IDiagnosticHubClient.SubscribeEvents), async () => await SubscribeEvents());

        _hubConn.On(nameof(IDiagnosticHubClient.UnsubscribeEvents), async () => await UnsubscribeEvents());
    }

    public Task SubscribeEvents()
    {
        lock (_eventLock)
        {
            // Tear down any prior subscription first, else its CTS and SendEventStream loop leak.
            StopEventStreamNoLock();

            CancellationTokenSource cts = new();
            _writeEventCancel = cts;
            _writeEventTask = Task.Run(() => SendEventStream(cts.Token), cts.Token);
        }

        return Task.CompletedTask;
    }

    public Task UnsubscribeEvents()
    {
        lock (_eventLock)
        {
            StopEventStreamNoLock();
        }

        return Task.CompletedTask;
    }

    // Client results: the value is returned from the invocation and SignalR carries it back to
    // the caller. Previously each of these built an RpcResult, serialised it by hand and pushed it
    // to a matching *Return method on the server, which correlated it by request id.
    //
    // All three go through Run, which puts them on the thread pool and serializes them; see its
    // remarks for why each half is needed.
    // cancel is unused by design, and unusable: see the handler registrations in the
    // constructor. It is on the interface because the SERVICE needs somewhere to put the caller's
    // token, and it never crosses the wire.
    public Task<DiagnosticResponse> GetDiagnostics(CancellationToken cancel)
    {
        return Run(() => DiagnosticManager.GetDiagnostics());
    }

    public Task<OperationResponse> SetProperty(string path, string value, CancellationToken cancel)
    {
        return Run(() => DiagnosticManager.SetProperty(path, value));
    }

    public Task<OperationResponse> ExecuteOperation(
        string path,
        string operation,
        string[] arguments,
        CancellationToken cancel
    )
    {
        return Run(() => DiagnosticManager.ExecuteOperation(path, operation, arguments));
    }

    /// <summary>
    ///     Runs a client-result handler off the SignalR receive loop, one at a time, logging
    ///     locally before letting the exception travel back to the caller.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Off the receive loop because these walk the whole registered object graph through
    ///         reflection, and blocking that loop stalls every other message on the connection,
    ///         including the keep-alive.
    ///     </para>
    ///     <para>
    ///         One at a time because the SignalR client deliberately does not await a handler that
    ///         returns a value - it will not block user code behind a client result - so without
    ///         _requestGate every web user's SetProperty and ExecuteOperation would drive
    ///         PropertyInfo.SetValue and MethodInfo.Invoke against the host's own objects
    ///         concurrently, and concurrently with the poll's read walk of the same graph. The
    ///         one-way sends this replaced were serialized by the client's invocation loop, so
    ///         registering an object never used to expose it to concurrent access. It still
    ///         doesn't.
    ///     </para>
    ///     <para>
    ///         The rethrow is the point: SignalR turns it into a failed invocation on the service
    ///         side, which is what replaces the hand-built failure RpcResult. Logging first keeps
    ///         the stack trace where the fault actually happened, on the agent.
    ///     </para>
    /// </remarks>
    [SuppressMessage(
        "Design",
        "S2139:Exceptions should be either logged or rethrown but not both",
        Justification = "Both are wanted, and they serve different readers. The log keeps the full "
            + "stack trace on the agent, where the fault happened; the rethrow is what makes SignalR "
            + "fail the invocation so the service sees the failure at all. Wrapping instead would "
            + "hide the original type from the caller."
    )]
    private async Task<T> Run<T>(Func<T> work)
    {
        // No token: nothing here can be cancelled. The agent never receives the caller's token,
        // and work() is synchronous reflection that does not observe one either. A queued request
        // waits for the one ahead of it; the SERVICE bounds how long its own caller waits.
        await _requestGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                    () =>
                    {
                        try
                        {
                            return work();
                        }
                        catch (Exception ex)
                        {
                            _log.Error(ex);
                            throw;
                        }
                    },
                    CancellationToken.None
                )
                .ConfigureAwait(false);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public void Dispose()
    {
        UnsubscribeEvents();
    }

    private void StopEventStreamNoLock()
    {
        var cts = _writeEventCancel;
        var task = _writeEventTask;
        _writeEventCancel = null;
        _writeEventTask = null;

        if (cts == null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A concurrent teardown already released the cancellation source.
        }

        // Dispose the CTS only after the stream task observes cancellation and completes, so we
        // never dispose a token still registered in an in-flight await (channel read / Invoke).
        if (task != null)
        {
            task.ContinueWith(_ => cts.Dispose(), TaskScheduler.Default);
        }
        else
        {
            cts.Dispose();
        }
    }

    /// <summary>
    ///     Streams this process's log events to the service, from
    ///     <see cref="DiagnosticManager.LogEventStore" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The source is the log-event store, not EventSinkRepo. The store carries the routing
    ///         in force and a sequence number per event, which is what lets the service replay a
    ///         reconnecting subscriber rather than starting it blank; a sink stream carried neither.
    ///     </para>
    ///     <para>
    ///         The consequence is that the legacy log4net DiagnosticAppender, which writes only to
    ///         EventSinkRepo, no longer feeds the realtime view. That is deliberate for 4.0.0: the
    ///         release already requires agents to be rebuilt, so a host wanting realtime events
    ///         moves to RoutingDiagnosticAppender or one of the NLog / Serilog /
    ///         Microsoft.Extensions.Logging adapters at the same time.
    ///     </para>
    ///     <para>
    ///         The batching is the store's, not a sleep: WaitToReadAsync blocks until there is
    ///         something, then the drain takes whatever else has arrived, up to a frame's worth.
    ///     </para>
    ///     <para>
    ///         The outer loop is what makes a dropped subscription recoverable. LogEventStore drops
    ///         a subscriber whose bounded channel is full rather than stall the caller's logging
    ///         thread, and completes its channel; a burst that outruns delivery therefore ends the
    ///         inner loop normally, with no exception. Without re-subscribing, that would silently
    ///         end event delivery for the life of the process, and every browser would sit on a
    ///         stale view with nothing to say why. Re-subscribing costs a fresh initialization,
    ///         which the service merges into what it already holds, so the gap closes rather than
    ///         the history resetting.
    ///     </para>
    /// </remarks>
    private async Task SendEventStream(CancellationToken cancel)
    {
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                await SendOneSubscription(cancel);

                if (cancel.IsCancellationRequested)
                {
                    break;
                }

                System.Diagnostics.Trace.TraceWarning(
                    "HubServerAdapter.SendEventStream: the log event subscription was dropped, most "
                        + "likely because events were published faster than they could be delivered. "
                        + "Re-subscribing; events published during the gap are recovered from the "
                        + "agent's retained window."
                );

                // Not a throughput knob: it stops a store that drops every subscription instantly
                // from turning this into a hot loop.
                await Task.Delay(TimeSpan.FromSeconds(1), cancel);
            }
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Trace.TraceInformation("HubServerAdapter.SendEventStream cancelled");
        }
        catch (Exception ex)
        {
            // A non-cancellation fault here ends event delivery to this client. The task is launched
            // fire-and-forget (Task.Run in SubscribeEvents; the disposal continuation discards it), so
            // without this catch the exception would go unobserved. Surface it rather than swallow it.
            System.Diagnostics.Trace.TraceError($"HubServerAdapter.SendEventStream failed: {ex}");
        }
    }

    /// <summary>Runs one subscription until its channel completes or the token is cancelled.</summary>
    /// <remarks>
    ///     The initialization is sent WITHOUT its replay events, and the replay follows through the
    ///     ordinary StreamLogEvents path. Sending it whole would put the entire retained window
    ///     (5 000 events by default) into a single hub message against a 10 MB receive cap, and the
    ///     burst that fills that window is the one carrying stack traces — so the frame would be
    ///     largest exactly when it is needed. Over the cap, the invocation faults, and a fault here
    ///     is not cancellation, so it ends delivery for the life of the connection. Batching it
    ///     through the same bounded path as live events removes the unbounded frame instead of
    ///     tuning its size. The service keys on sequence, so a replayed event it already holds is
    ///     dropped either way.
    /// </remarks>
    private async Task SendOneSubscription(CancellationToken cancel)
    {
        // The store sizes the live channel from its own retention. That widens the gap this
        // replay can survive - it is up to one round trip per hundred retained events, and the
        // channel has to absorb everything the process logs meanwhile - but it does not close it:
        // a process logging faster than the frames drain still overflows, is dropped, and is
        // recovered by the outer loop's re-subscribe rather than lost, provided the gap stays
        // inside the retained window.
        using var stream = EventStore.CreateSubscription();

        // CreateSubscription always sets this before returning; the property is nullable only
        // because the subscription is constructed before its snapshot is taken. The fallback still
        // carries the store's own stream id, because an initialization without one is not something
        // the service can key events against - it rejects it.
        var initialization = stream.Initialization ?? new LogStreamInitialization { StreamId = EventStore.StreamId };

        var replay = initialization.ReplayEvents ?? [];
        initialization.ReplayEvents = [];

        await _hubConn.InvokeCoreAsync(
            nameof(IDiagnosticHubServer.InitializeLogStream),
            typeof(object),
            new object[] { initialization },
            cancel
        );

        for (var sent = 0; sent < replay.Length; sent += MaxEventsPerFrame)
        {
            var frame = replay.Skip(sent).Take(MaxEventsPerFrame).ToArray();
            await _hubConn.InvokeCoreAsync(
                nameof(IDiagnosticHubServer.StreamLogEvents),
                typeof(object),
                new object[] { frame },
                cancel
            );
        }

        while (await stream.Events.WaitToReadAsync(cancel))
        {
            List<LogStreamEvent> batch = [];
            while (batch.Count < MaxEventsPerFrame && stream.Events.TryRead(out var streamEvent))
            {
                batch.Add(streamEvent);
            }

            if (batch.Count == 0)
            {
                continue;
            }

            await _hubConn.InvokeCoreAsync(
                nameof(IDiagnosticHubServer.StreamLogEvents),
                typeof(object),
                new object[] { batch.ToArray() },
                cancel
            );
        }
    }

    public async Task<RegistrationResponse> Register(Registration registration, CancellationToken cancel = default)
    {
        var response = await _hubConn.InvokeCoreAsync<RpcResult<RegistrationResponse>>(
            nameof(IDiagnosticHubServer.Register),
            new object[] { registration },
            cancel
        );
        if (!response.IsSuccess)
        {
            throw new InvalidOperationException(response.Message);
        }

        return response.Response;
    }

    public async Task Deregister(Registration registration, CancellationToken cancel = default)
    {
        var response = await _hubConn.InvokeCoreAsync<RpcResult>(
            nameof(IDiagnosticHubServer.Deregister),
            new object[] { registration },
            cancel
        );
        if (!response.IsSuccess)
        {
            throw new InvalidOperationException(response.Message);
        }
    }

    // Sends the messages as a typed array. MessagePack frames them on the wire, so the
    // serialise-then-gzip step this used to do by hand is gone.
    public async Task LogEvents(IList<DiagnosticMsg> messages, CancellationToken cancel = default)
    {
        var response = await _hubConn.InvokeCoreAsync<RpcResult>(
            nameof(IDiagnosticHubServer.LogEvents),
            new object[] { messages as DiagnosticMsg[] ?? [.. messages] },
            cancel
        );

        if (!response.IsSuccess)
        {
            throw new InvalidOperationException(response.Message);
        }
    }
}
