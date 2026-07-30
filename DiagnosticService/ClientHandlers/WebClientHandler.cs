using System.Collections.Concurrent;
using System.Diagnostics;
using Diagnostic.Service.Common;
using Diagnostic.Service.Hubs;
using DiagnosticExplorer.Events;
using DiagnosticExplorer.Interface;

namespace Diagnostic.Service.ClientHandlers;

public class WebClientHandler
{
    private readonly IWebHubClient _client;
    private readonly object _eventStreamLock = new();
    private readonly ConcurrentDictionary<string, EventStreamState> _eventStreams = new();
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

        lock (_eventStreamLock)
        {
            foreach (var kvp in _eventStreams)
            {
                kvp.Value.Cancel.Cancel();
            }

            _eventStreams.Clear();
        }
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
                            Trace.TraceError(
                                $"WebClientHandler {ConnectionId} send failed: {ex.Message}"
                            );
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

    public async Task SetEvents(string id, SystemEvent[] events)
    {
        await _client.SetEvents(id, events);
    }

    public async Task ShowDiagnosticsError(string id, string message)
    {
        await _client.ShowDiagnosticsError(id, message);
    }

    public void StartStreamingEvents(string id, EventSinkRepo sinkRepo)
    {
        lock (_eventStreamLock)
        {
            if (_eventStreams.TryRemove(id, out var existingState))
            {
                existingState.Cancel.Cancel();
            }

            CancellationTokenSource cancelSource = new();
            var task = StreamEvents(id, sinkRepo, cancelSource.Token);
            var state = new EventStreamState(task, cancelSource);
            _eventStreams[id] = state;
            _ = ObserveEventStream(id, task, cancelSource);
        }
    }

    public void StopStreamingEvents(string id)
    {
        lock (_eventStreamLock)
        {
            if (_eventStreams.TryRemove(id, out var state))
            {
                state.Cancel.Cancel();
            }
        }
    }

    private async Task StreamEvents(string id, EventSinkRepo sinkRepo, CancellationToken cancel)
    {
        using var stream = sinkRepo.CreateSinkStream(TimeSpan.FromMilliseconds(25), 100);
        try
        {
            await _client.SetEvents(id, stream.InitialEvents);

            while (!cancel.IsCancellationRequested)
            {
                IList<SystemEvent>? evts = await stream.EventChannel.Reader.ReadAsync(cancel);
                if (evts != null)
                {
                    await _client.StreamEvents(id, evts);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected when the event stream is cancelled via Stop()
        }
        catch (Exception ex)
        {
            Trace.TraceError(
                $"WebClientHandler {ConnectionId} event stream failed, delivery stopped: {ex.Message}"
            );
        }
    }

    private async Task ObserveEventStream(
        string id,
        Task eventStreamTask,
        CancellationTokenSource eventStreamCancel
    )
    {
        using (eventStreamCancel)
        {
            try
            {
                await eventStreamTask;
            }
            finally
            {
                lock (_eventStreamLock)
                {
                    if (
                        _eventStreams.TryGetValue(id, out var state)
                        && ReferenceEquals(state.Task, eventStreamTask)
                    )
                    {
                        _eventStreams.TryRemove(id, out _);
                    }
                }
            }
        }
    }

    private sealed class EventStreamState
    {
        public EventStreamState(Task task, CancellationTokenSource cancel)
        {
            Task = task;
            Cancel = cancel;
        }

        public Task Task { get; }
        public CancellationTokenSource Cancel { get; }
    }
}
