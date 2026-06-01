using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiagnosticExplorer;
using DiagnosticExplorer.Common;
using Diagnostics.Service.Common.Hubs;

namespace DiagWebService.ClientHandlers;

public class WebClientHandler
{
    private IWebHubClient _client;
    private IDisposable? _processSubscription;
    private IDisposable? _processRemoveSubscription;
    private readonly object _sendLock = new();
    private Task _sendChain = Task.CompletedTask;
    private Task? _eventStreamTask;
    private CancellationTokenSource? _eventStreamCancel;
    private readonly object _eventStreamLock = new();
    private string? _pendingEventStreamId;
    private EventSinkRepo? _pendingEventStreamRepo;

    public WebClientHandler(string connectionId, IWebHubClient client)
    {
        ConnectionId = connectionId;
        _client = client;
    }


    public string ConnectionId { get; }

    public void Start(RealtimeManager realtimeManager)
    {
        _client.SetProcesses(realtimeManager.GetProcesses().ToArray());
        _processSubscription = realtimeManager.ProcessChanged.Subscribe(HandleProcessesChanged);
        _processRemoveSubscription = realtimeManager.ProcessRemoved.Subscribe(HandleProcessRemoved);
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

    // Serialize the per-client SignalR sends and observe their faults. The source
    // ProcessChanged/ProcessRemoved subjects are Subject.Synchronize'd, so callbacks arrive in order;
    // chaining preserves that order on the wire (an unawaited send could otherwise complete out of
    // order — e.g. an update landing after the remove it preceded) and stops a failed send from being
    // lost silently. (A10)
    private void EnqueueSend(Func<Task> send)
    {
        lock (_sendLock)
        {
            _sendChain = _sendChain.ContinueWith(async _ =>
            {
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
        //Debug.WriteLine($"########## WebClientHandler.StartStreamingEvents connection {ConnectionId}");
        lock (_eventStreamLock)
        {
            if (_eventStreamTask is { IsCompleted: false })
            {
                if (_eventStreamCancel?.IsCancellationRequested == true)
                {
                    _pendingEventStreamId = id;
                    _pendingEventStreamRepo = sinkRepo;
                }

                return;
            }

            _pendingEventStreamId = null;
            _pendingEventStreamRepo = null;
            StartStreamingEventsLocked(id, sinkRepo);
        }
    }

    public void StopStreamingEvents()
    {
        //Debug.WriteLine($"########## WebClientHandler.StopStreamingEvents {ConnectionId}");
        lock (_eventStreamLock)
        {
            _pendingEventStreamId = null;
            _pendingEventStreamRepo = null;
            _eventStreamCancel?.Cancel();
        }
    }

    private void StartStreamingEventsLocked(string id, EventSinkRepo sinkRepo)
    {
        CancellationTokenSource eventStreamCancel = new();
        Task eventStreamTask = StreamEvents(id, sinkRepo, eventStreamCancel.Token);
        _eventStreamCancel = eventStreamCancel;
        _eventStreamTask = eventStreamTask;
        _ = ObserveEventStream(eventStreamTask, eventStreamCancel);
    }

    private async Task StreamEvents(string id, EventSinkRepo sinkRepo, CancellationToken cancel)
    {
        using EventSinkStream? stream = sinkRepo.CreateSinkStream(TimeSpan.FromMilliseconds(25), 100);
        try
        {
            //Debug.WriteLine($"########## WebClientHandler calling _client.SetEvents({id}, {stream.InitialEvents.Length} events)");
            await _client.SetEvents(id, stream.InitialEvents);

            while (!cancel.IsCancellationRequested)
            {
                IList<SystemEvent>? evts = await stream.EventChannel.Reader.ReadAsync(cancel);
                if (evts != null)
                {
                    await _client.StreamEvents(id, evts);
                    //Debug.WriteLine($"########## WebClientHandler calling _client.StreamEvent({id}, 1 event)");
                }
            }
        }
        catch (OperationCanceledException)
        {
            //Debug.WriteLine("########## Stream event task cancelled");
        }
        catch (Exception ex)
        {
            // Don't swallow silently: a non-cancellation failure here ends event delivery to this
            // client. In practice it coincides with the SignalR connection dropping (handled by
            // OnDisconnectedAsync, which removes the web client). Surfacing it makes the otherwise
            // invisible "events just stopped" case diagnosable. (Automatic stream restart is a
            // deferred follow-up — it needs loop-guarding to avoid hammering a broken client.)
            Trace.WriteLine($"WebClientHandler {ConnectionId} event stream failed, delivery stopped: {ex.Message}");
        }
    }

    private async Task ObserveEventStream(Task eventStreamTask, CancellationTokenSource eventStreamCancel)
    {
        try
        {
            await eventStreamTask;
        }
        finally
        {
            lock (_eventStreamLock)
            {
                if (ReferenceEquals(_eventStreamTask, eventStreamTask))
                {
                    _eventStreamTask = null;
                    if (_pendingEventStreamId != null && _pendingEventStreamRepo != null)
                    {
                        string pendingEventStreamId = _pendingEventStreamId;
                        EventSinkRepo pendingEventStreamRepo = _pendingEventStreamRepo;
                        _pendingEventStreamId = null;
                        _pendingEventStreamRepo = null;
                        StartStreamingEventsLocked(pendingEventStreamId, pendingEventStreamRepo);
                    }
                }

                if (ReferenceEquals(_eventStreamCancel, eventStreamCancel))
                    _eventStreamCancel = null;
            }

            eventStreamCancel.Dispose();
        }
    }

}
