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

    private readonly HubConnection _hubConn;
    private CancellationTokenSource? _writeEventCancel;
    private Task? _writeEventTask;

    public HubServerAdapter(HubConnection hubConn)
    {
        _hubConn = hubConn;

        // Registered through the value-returning On overloads, which is what makes these client
        // results rather than one-way notifications.
        _hubConn.On<DiagnosticResponse>(nameof(IDiagnosticHubClient.GetDiagnostics), GetDiagnostics);

        _hubConn.On<string, string, OperationResponse>(
            nameof(IDiagnosticHubClient.SetProperty),
            (path, value) => SetProperty(path, value)
        );

        _hubConn.On<string, string, string[], OperationResponse>(
            nameof(IDiagnosticHubClient.ExecuteOperation),
            (path, operation, args) => ExecuteOperation(path, operation, args)
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
    // Still on the thread pool via Task.Run: these walk the whole registered object graph through
    // reflection, and the SignalR client invokes handlers on its receive loop. Blocking that loop
    // stalls every other message on the connection, including the keep-alive.
    public Task<DiagnosticResponse> GetDiagnostics()
    {
        return Run(() => DiagnosticManager.GetDiagnostics());
    }

    public Task<OperationResponse> SetProperty(string path, string value)
    {
        return Run(() => DiagnosticManager.SetProperty(path, value));
    }

    public Task<OperationResponse> ExecuteOperation(string path, string operation, string[] arguments)
    {
        return Run(() => DiagnosticManager.ExecuteOperation(path, operation, arguments));
    }

    /// <summary>
    ///     Runs a client-result handler off the SignalR receive loop, logging locally before
    ///     letting the exception travel back to the caller.
    /// </summary>
    /// <remarks>
    ///     The rethrow is the point: SignalR turns it into a failed invocation on the service
    ///     side, which is what replaces the hand-built failure RpcResult. Logging first keeps the
    ///     stack trace where the fault actually happened, on the agent.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "S2139:Exceptions should be either logged or rethrown but not both",
        Justification = "Both are wanted, and they serve different readers. The log keeps the full "
            + "stack trace on the agent, where the fault happened; the rethrow is what makes SignalR "
            + "fail the invocation so the service sees the failure at all. Wrapping instead would "
            + "hide the original type from the caller."
    )]
    private static Task<T> Run<T>(Func<T> work)
    {
        return Task.Run(
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
        );
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
