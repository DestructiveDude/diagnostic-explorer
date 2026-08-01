using System.Diagnostics;
using Diagnostic.Service.ClientHandlers;
using DiagnosticExplorer;
using DiagnosticExplorer.Events;
using DiagnosticExplorer.Interface;
using DiagnosticExplorer.Util;
using log4net;
using Microsoft.AspNetCore.SignalR;

namespace Diagnostic.Service.Hubs;

public class DiagnosticHub : Hub<IDiagnosticHubClient>, IDiagnosticHubServer
{
    private static readonly ILog _log = LogManager.GetLogger(typeof(DiagnosticHub));
    private static readonly AsyncResultBucket _clientResponses = new();
    private readonly RetroManager _retroManager;
    private readonly RealtimeManager _rtManager;

    public DiagnosticHub(RealtimeManager rtManager, RetroManager retroManager)
    {
        _rtManager = rtManager;
        _retroManager = retroManager;
    }

    // Not async: the body is synchronous (CS1998). SignalR awaits the returned Task either way.
    public Task<RpcResult<RegistrationResponse>> Register(Registration registration)
    {
        RegistrationResponse response = new(TimeSpan.FromSeconds(20));
        try
        {
            _rtManager.Register(registration, Context.ConnectionId);
            return Task.FromResult(RpcResult<RegistrationResponse>.Success(response));
        }
        catch (Exception ex)
        {
            return Task.FromResult(RpcResult<RegistrationResponse>.Fail(null, ex.Message, ex.ToString()));
        }
    }

    public Task<RpcResult> Deregister(Registration registration)
    {
        try
        {
            _rtManager.Deregister(registration);
            return Task.FromResult(RpcResult.Success());
        }
        catch (Exception ex)
        {
            return Task.FromResult(RpcResult.Fail(null, ex));
        }
    }

    // Not async: the body is synchronous (CS1998). SignalR awaits the returned Task either way.
    public Task<RpcResult> LogEvents(byte[] eventData)
    {
        try
        {
            var messages = ProtobufUtil.Decompress<DiagnosticMsg[]>(eventData);
            if (messages?.Any() == true)
            {
                _rtManager.RegisterAlertLevel(Context.ConnectionId, messages);

                _retroManager.LogEvents(messages);
            }

            return Task.FromResult(RpcResult.Success());
        }
        catch (Exception ex)
        {
            _log.Error(ex);
            return Task.FromResult(RpcResult.Fail(null, ex));
        }
    }

    public Task GetDiagnosticsReturn(RpcResult<byte[]> response)
    {
        _clientResponses.SetResult(response, response.Response);
        return Task.CompletedTask;
    }

    public Task ExecuteOperationReturn(RpcResult<OperationResponse> response)
    {
        _clientResponses.SetResult(response, response.Response);
        return Task.CompletedTask;
    }

    public Task SetPropertyReturn(RpcResult<OperationResponse> response)
    {
        return ExecuteOperationReturn(response);
    }

    public Task SetEvents(SystemEvent[] events)
    {
        // GetClientHandler returns null on a disconnect/registration race; guard like the other
        // hub methods rather than NRE-ing inside the invocation.
        _rtManager.GetClientHandler(Context.ConnectionId)?.SetEvents(events);
        return Task.CompletedTask;
    }

    public Task StreamEvents(SystemEvent[] evt)
    {
        _rtManager.GetClientHandler(Context.ConnectionId)?.StreamEvents(evt);
        return Task.CompletedTask;
    }

    public override Task OnConnectedAsync()
    {
        _rtManager.AddDiagnosticClient(new DiagnosticClientHandler(Context, Clients.Caller, _clientResponses));
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        Trace.TraceInformation("Disconnected");
        if (exception != null)
        {
            Trace.TraceError(exception.ToString());
        }

        return base.OnDisconnectedAsync(exception);
    }
}
