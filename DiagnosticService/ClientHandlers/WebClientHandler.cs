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
    private IDisposable? _processSubscription;
    private IDisposable? _processRemoveSubscription;
    private readonly object _sendLock = new();
    private Task _sendChain = Task.CompletedTask;
    private readonly ConcurrentDictionary<string, EventStreamState> _eventStreams = new();
    private readonly object _eventStreamLock = new();

    private class EventStreamState : IDisposable
    {
        public Task Task { get; }
        public CancellationTokenSource Cancel { get; }

        public EventStreamState(Task task, CancellationTokenSource cancel)
        {
            Task = task;
            Cancel = cancel;
        }

        public void Dispose()
        {
            Cancel.Dispose();
        }
    }

    public WebClientHandler(string connectionId, IWebHubClient client)
    {
        ConnectionId = connectionId;
        _client = client;
    }


    public string ConnectionId { get; }

    public void Start(RealtimeManager realtimeManager)
    {
        // Enqueue snapshot through _sendChain before subscribing so change/remove events
        // that arrive immediately after subscription are serialised after the initial list.
        DiagProcess[] processes = realtimeManager.GetProcesses().ToArray();
        EnqueueSend(() => _client.SetProcesses(processes));
        _processSubscription = realtimeManager.ProcessChanged.Subscribe(HandleProcessesChanged);
        _processRemoveSubscription = realtimeManager.ProcessRemoved.Subscribe(HandleProcessRemoved);
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

    // Serialize the per-client SignalR sends and observe their faults. The source
    // ProcessChanged/ProcessRemoved subjects are Subject.Synchronize'd, so callbacks arrive in order;
    // chaining preserves that order on the wire (an unawaited send could otherwise complete out of
    // order — e.g. an update landing after the remove it preceded) and stops a failed send from being
    // lost silently. (A10)
    private void EnqueueSend(Func<Task> send)
    {
        lock (_sendLock)
        {
            _sendChain = _sendChain.ContinueWith(async _ => {
                try { await send(); }
                catch (Exception ex) { Trace.WriteLine($"WebClientHandler {ConnectionId} send failed: {ex.Message}"); }
            }, TaskScheduler.Default).Unwrap();
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
            Task task = StreamEvents(id, sinkRepo, cancelSource.Token);
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
        using EventSinkStream? stream = sinkRepo.CreateSinkStream(TimeSpan.FromMilliseconds(25), 100);
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
            Trace.WriteLine($"WebClientHandler {ConnectionId} event stream failed, delivery stopped: {ex.Message}");
        }
    }

    private async Task ObserveEventStream(string id, Task eventStreamTask, CancellationTokenSource eventStreamCancel)
    {
        try
        {
            await eventStreamTask;
        }
        finally
        {
            lock (_eventStreamLock)
            {
                if (_eventStreams.TryGetValue(id, out var state) && ReferenceEquals(state.Task, eventStreamTask))
                {
                    _eventStreams.TryRemove(id, out _);
                }
            }

            eventStreamCancel.Dispose();
        }
    }

}
