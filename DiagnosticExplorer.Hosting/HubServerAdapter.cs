#nullable enable annotations

using System;
using System.Threading;
using System.Threading.Tasks;
using DiagnosticExplorer.Events;
using DiagnosticExplorer.Interface;
using DiagnosticExplorer.Util;
using log4net;
using Microsoft.AspNetCore.SignalR.Client;

namespace DiagnosticExplorer.Hosting;

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

        _hubConn.On<string>(
            nameof(IDiagnosticHubClient.GetDiagnostics),
            async requestId => await GetDiagnostics(requestId)
        );

        _hubConn.On<string, string, string>(
            nameof(IDiagnosticHubClient.SetProperty),
            async (requestId, context, value) => await SetProperty(requestId, context, value)
        );

        _hubConn.On<string, string, string, string[]>(
            nameof(IDiagnosticHubClient.ExecuteOperation),
            async (requestId, path, operation, args) =>
                await ExecuteOperation(requestId, path, operation, args)
        );

        _hubConn.On(
            nameof(IDiagnosticHubClient.SubscribeEvents),
            async () => await SubscribeEvents()
        );

        _hubConn.On(
            nameof(IDiagnosticHubClient.UnsubscribeEvents),
            async () => await UnsubscribeEvents()
        );
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

    public Task GetDiagnostics(string requestId)
    {
        return Task.Run(async () =>
        {
            RpcResult<byte[]> result;
            try
            {
                var response = DiagnosticManager.GetDiagnostics();
                var compress = ProtobufUtil.Compress(response, 1024);

                result = RpcResult<byte[]>.Success(requestId, compress);
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                result = RpcResult<byte[]>.Fail(requestId, ex);
            }

            await _hubConn.InvokeCoreAsync<string>(
                nameof(IDiagnosticHubServer.GetDiagnosticsReturn),
                new object[] { result }
            );
        });
    }

    public Task SetProperty(string requestId, string path, string value)
    {
        return Task.Run(async () =>
        {
            RpcResult<OperationResponse> result;

            try
            {
                var response = DiagnosticManager.SetProperty(path, value);
                result = RpcResult<OperationResponse>.Success(requestId, response);
            }
            catch (Exception ex)
            {
                result = RpcResult<OperationResponse>.Fail(requestId, ex);
            }
            await _hubConn.InvokeCoreAsync<string>(
                nameof(IDiagnosticHubServer.SetPropertyReturn),
                new object[] { result }
            );
        });
    }

    public Task ExecuteOperation(
        string requestId,
        string path,
        string operation,
        string[] arguments
    )
    {
        return Task.Run(async () =>
        {
            RpcResult<OperationResponse> result;

            try
            {
                var response = DiagnosticManager.ExecuteOperation(path, operation, arguments);
                result = RpcResult<OperationResponse>.Success(requestId, response);
            }
            catch (Exception ex)
            {
                result = RpcResult<OperationResponse>.Fail(requestId, ex);
            }
            await _hubConn.InvokeCoreAsync<string>(
                nameof(IDiagnosticHubServer.ExecuteOperationReturn),
                new object[] { result }
            );
        });
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
        using var stream = EventSinkRepo.Default.CreateSinkStream(
            TimeSpan.FromMilliseconds(50),
            100
        );

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

    public async Task<RegistrationResponse> Register(
        Registration registration,
        CancellationToken cancel = default
    )
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

    public async Task LogEvents(byte[] eventData, CancellationToken cancel = default)
    {
        var response = await _hubConn.InvokeCoreAsync<RpcResult>(
            nameof(IDiagnosticHubServer.LogEvents),
            new object[] { eventData },
            cancel
        );

        if (!response.IsSuccess)
        {
            throw new InvalidOperationException(response.Message);
        }
    }
}
