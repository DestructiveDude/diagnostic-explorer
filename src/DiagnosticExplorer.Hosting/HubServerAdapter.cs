#nullable enable annotations

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
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

    // _requestGate serializes the three client results against each other. See Run.
    private readonly SemaphoreSlim _requestGate = new(1, 1);

    private readonly HubConnection _hubConn;
    private CancellationTokenSource? _writeEventCancel;
    private Task? _writeEventTask;

    public HubServerAdapter(HubConnection hubConn)
    {
        _hubConn = hubConn;

        // Registered through the value-returning On overloads, which is what makes these client
        // results rather than one-way notifications.
        // The service's typed proxy strips the trailing CancellationToken from the wire
        // arguments (TypedClientBuilder), so these handlers still receive only the declared ones.
        // The token bounds the caller's wait, not the agent's work.
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
    public Task<DiagnosticResponse> GetDiagnostics(CancellationToken cancel)
    {
        return Run(() => DiagnosticManager.GetDiagnostics(), cancel);
    }

    public Task<OperationResponse> SetProperty(string path, string value, CancellationToken cancel)
    {
        return Run(() => DiagnosticManager.SetProperty(path, value), cancel);
    }

    public Task<OperationResponse> ExecuteOperation(
        string path,
        string operation,
        string[] arguments,
        CancellationToken cancel
    )
    {
        return Run(() => DiagnosticManager.ExecuteOperation(path, operation, arguments), cancel);
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
    private async Task<T> Run<T>(Func<T> work, CancellationToken cancel)
    {
        await _requestGate.WaitAsync(cancel).ConfigureAwait(false);
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
        _requestGate.Dispose();
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

    private async Task SendEventStream(CancellationToken cancel)
    {
        using var stream = EventSinkRepo.Default.CreateSinkStream(TimeSpan.FromMilliseconds(50), 100);

        try
        {
            var initial = stream.InitialEvents;
            await _hubConn.InvokeCoreAsync<string>(
                nameof(IDiagnosticHubServer.SetEvents),
                new object[] { initial },
                cancel
            );

            while (await stream.EventChannel.Reader.WaitToReadAsync(cancel))
            {
                var item = await stream.EventChannel.Reader.ReadAsync(cancel);
                await _hubConn.InvokeCoreAsync<string>(
                    nameof(IDiagnosticHubServer.StreamEvents),
                    new object[] { item },
                    cancel
                );
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
