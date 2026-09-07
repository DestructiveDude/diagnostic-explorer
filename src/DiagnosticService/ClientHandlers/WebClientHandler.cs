using System.Diagnostics;
using Diagnostic.Service.Common;
using Diagnostic.Service.Hubs;
using DiagnosticExplorer;
using DiagnosticExplorer.Logging;

namespace Diagnostic.Service.ClientHandlers;

public class WebClientHandler
{
    private readonly IWebHubClient _client;
    private readonly object _sendLock = new();
    private IDisposable? _processRemoveSubscription;
    private IDisposable? _processSubscription;
    private Task _sendChain = Task.CompletedTask;

    public WebClientHandler(string connectionId, IWebHubClient client)
    {
        ConnectionId = connectionId;
        _client = client;
    }

    public string ConnectionId { get; }

    public void Start(RealtimeManager realtimeManager)
    {
        _processSubscription = realtimeManager.ProcessChanged.Subscribe(HandleProcessesChanged);
        _processRemoveSubscription = realtimeManager.ProcessRemoved.Subscribe(HandleProcessRemoved);
        var processes = realtimeManager.GetProcesses().ToArray();
        EnqueueSend(() => _client.SetProcesses(processes));
    }

    public void Stop()
    {
        _processSubscription?.Dispose();
        _processRemoveSubscription?.Dispose();
    }

    private void HandleProcessesChanged(DiagProcess changed)
    {
        EnqueueSend(() => _client.UpdateProcess(changed));
    }

    private void HandleProcessRemoved(DiagProcess changed)
    {
        EnqueueSend(() => _client.RemoveProcess(changed.Id));
    }

    /// <summary>
    /// Serializes per-client SignalR sends. The synchronized source subjects preserve callback
    /// order, and this chain preserves that order on the wire while observing send failures.
    /// </summary>
    private void EnqueueSend(Func<Task> send)
    {
        lock (_sendLock)
        {
            _sendChain = _sendChain
                .ContinueWith(
                    async _ =>
                    {
                        try
                        {
                            await send();
                        }
                        catch (Exception ex)
                        {
                            Trace.TraceError($"WebClientHandler {ConnectionId} send failed: {ex.Message}");
                        }
                    },
                    TaskScheduler.Default
                )
                .Unwrap();
        }
    }

    public async Task ShowDiagnostics(string id, DiagnosticResponse response)
    {
        await _client.ShowDiagnostics(id, response);
    }

    public async Task ShowDiagnosticsError(string id, string message)
    {
        await _client.ShowDiagnosticsError(id, message);
    }

    /// <summary>Sends a process's stream snapshot, replacing whatever the browser holds for it.</summary>
    /// <remarks>
    ///     Enqueued rather than awaited, for two reasons. It is called from inside
    ///     DiagnosticSubscription's start/stop lock, so it must not block; and the send chain keeps
    ///     it ahead of the StreamLogEvents that follow, which matters because an initialization
    ///     that arrived after its own live events would discard them.
    /// </remarks>
    public void InitializeLogStream(string id, LogStreamInitialization initialization)
    {
        EnqueueSend(() => _client.InitializeLogStream(id, initialization));
    }

    /// <summary>Sends live events for a process.</summary>
    public void StreamLogEvents(string id, LogStreamEvent[] events)
    {
        EnqueueSend(() => _client.StreamLogEvents(id, events));
    }
}
