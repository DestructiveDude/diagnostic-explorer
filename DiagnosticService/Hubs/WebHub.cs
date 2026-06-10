using System.Diagnostics;
using Diagnostic.Service.Transport;
using DiagnosticExplorer.Interface;
using log4net;
using Microsoft.AspNetCore.SignalR;

namespace Diagnostic.Service.Hubs;

public class WebHub : Hub<IWebHubClient>
{
    private static readonly ILog _log = LogManager.GetLogger(typeof(WebHub));
    private readonly RealtimeManager _realtimeManager;
    private readonly RetroManager _retroManager;

    public WebHub(RealtimeManager realtimeManager, RetroManager retroManager)
    {
        _realtimeManager = realtimeManager;
        _retroManager = retroManager;
    }

    public override async Task OnConnectedAsync()
    {
        Debug.WriteLine($"WebHub OnConnectedAsync {Context.ConnectionId}");
        await base.OnConnectedAsync();
        _realtimeManager.AddWebHubClient(Context.ConnectionId, Clients.Caller);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        Debug.WriteLine($"WebHub OnDisconnectedAsync {exception}");
        _realtimeManager.RemoveWebHubClient(Context.ConnectionId);
        return Task.WhenAll(
            _retroManager.CancelConnectionSearch(Context.ConnectionId),
            base.OnDisconnectedAsync(exception));
    }

    public async Task Subscribe(string processId)
    {
        await _realtimeManager.SubscribeWebClient(Context.ConnectionId, processId);
    }

    public Task RemoveProcess(string processId)
    {
        _realtimeManager.RemoveProcess(processId);
        return Task.CompletedTask;
    }

    public async Task<OperationResponse> SetProperty(SetPropertyRequest request)
    {
        return await _realtimeManager.SetProperty(request);
    }

    public async Task<OperationResponse> ExecuteOperation(ExecuteOperationRequest request)
    {
        return await _realtimeManager.ExecuteOperation(request);
    }

    public Task StartRetroSearch(RetroQuery query)
    {
        return _retroManager.StartRetroSearch(query, Context.ConnectionId, Clients.Caller);
    }

    public Task<long> RetroDelete(string[] recordList)
    {
        return _retroManager.RetroDelete(recordList);
    }

    /// <summary>
    /// Whether the active Retro backend supports per-record delete. The web client queries this
    /// once per connection and hides the delete affordance when false (append-only backends such
    /// as Log Analytics), so it never issues a delete that would fault.
    /// </summary>
    public bool RetroSupportsDelete()
    {
        return _retroManager.SupportsDelete;
    }

    public Task CancelRetroSearch(int searchId)
    {
        return _retroManager.CancelRetroSearch(searchId, Context.ConnectionId);
    }
}