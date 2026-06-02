using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using DiagnosticExplorer;
using DiagnosticExplorer.Common;

namespace DiagWebService.ClientHandlers;


public class DiagnosticSubscription
{
    private static int _instanceCounter = 0;
    private int _instance;
    public DiagProcess Process { get; set; }
    public IDiagnosticClient? DiagnosticClient { get; private set; }
    private readonly ConcurrentDictionary<string, WebClientHandler> _webClients = new();
    private Task? _requestLoop;
    private CancellationTokenSource? _requestLoopCancelSource;
    private DiagnosticResponse? _lastResponse;
    public string ProcessId => Process.Id;
    private IDiagnosticClient? _eventSubscriptionOwnerClient;
    private IDisposable? _eventSetSubscription;
    private IDisposable? _eventStreamSubscription;
    private readonly EventSinkRepo _eventRepo = new();
    private readonly object _startStopLock = new();
    private bool _streamingStarted = false;
    private bool _eventSubscriptionStopInProgress = false;
    private IDiagnosticClient? _eventSubscriptionStopClient;
    private bool _eventSubscriptionRestartBlocked = false;

    public DiagnosticSubscription(DiagProcess process)
    {
        _instance = Interlocked.Increment(ref _instanceCounter);
        Process = process;
    }

    public void SetDiagnosticClient(IDiagnosticClient? diagClient)
    {
        if (DiagnosticClient != diagClient)
        {
            IDiagnosticClient? previousClient = DiagnosticClient;
            lock (_startStopLock)
            {
                DiagnosticClient = diagClient;
                _eventSubscriptionRestartBlocked = true;
                StopRequestLoop();
            }

            StopDiagClientEvents(previousClient);
            StopWebClientEvents();
            lock (_startStopLock)
            {
                _eventSubscriptionRestartBlocked = false;
            }
            StartIfRequired();
        }
    }

    private void StopWebClientEvents()
    {
        WebClientHandler[] handlers;
        lock (_startStopLock)
        {
            _streamingStarted = false;
            handlers = _webClients.Values.ToArray();
        }

        foreach (WebClientHandler handler in handlers)
        {
            //Debug.WriteLine($"@@@@@@@@@@ StopWebClientEventStreaming handler {handler.ConnectionId} stop streaming events");
            handler.StopStreamingEvents(ProcessId);
        }
    }

    public async Task AddWebClient(WebClientHandler webClient)
    {
        //Debug.WriteLine($"@@@@@@@@@@ DiagnosticSubscription.AddWebClient {webClient.ConnectionId} now there are {_webClients.Count} _streamingStarted: {_streamingStarted}");

        if (_lastResponse != null)
            await TrySend(webClient, _lastResponse);

        if (DiagnosticClient == null)
            await webClient.SetEvents(ProcessId, _eventRepo.GetEvents());

        lock (_startStopLock)
        {
            bool added = _webClients.TryAdd(webClient.ConnectionId, webClient);

            if (added && _streamingStarted)
                webClient.StartStreamingEvents(Process.Id, _eventRepo);

            StartIfRequired();
        }
    }


    public void RemoveWebClient(WebClientHandler webClient)
    {
        if (_webClients.ContainsKey(webClient.ConnectionId))
        {
            //Debug.WriteLine($"@@@@@@@@@@ DiagnosticSubscription.RemoveWebClient {webClient.ConnectionId} currently {_webClients.Count} clients before removal");

            webClient.StopStreamingEvents(ProcessId);

            lock (_startStopLock)
            {
                _webClients.TryRemove(webClient.ConnectionId, out _);
            }

            StopIfRequired();
        }
    }

    private void StartIfRequired()
    {
        lock (_startStopLock)
        {
            if (_webClients.Any() && DiagnosticClient != null && _eventStreamSubscription == null
                && !_eventSubscriptionStopInProgress && !_eventSubscriptionRestartBlocked)
                StartDiagClientEvents();

            if (_webClients.Any() && DiagnosticClient != null && _requestLoop == null)
                StartRequestLoop();

        }
    }

    private void StartRequestLoop()
    {
        CancellationTokenSource cts = new();
        _requestLoopCancelSource = cts;
        Task loop = RunLoop(DiagnosticClient!, cts.Token);
        _requestLoop = loop;
        // Dispose this loop's CTS when the loop actually finishes (not in StopRequestLoop, where
        // the still-draining loop is using the token) — fixes the per-swap CTS leak.
        loop.ContinueWith(_ => cts.Dispose(), TaskScheduler.Default);
    }

    private void StopRequestLoop()
    {
        _requestLoopCancelSource?.Cancel();
        _requestLoopCancelSource = null;
        _requestLoop = null;
    }
    
    private void StartDiagClientEvents()
    {
        _eventRepo.Clear();
        IDiagnosticClient diagnosticClient = DiagnosticClient!;
        //Debug.WriteLine($"@@@@@@@@@@ DiagnosticSubscription StartEventSubscriptions");
        IDisposable? eventSetSubscription = null;
        IDisposable? eventStreamSubscription = null;
        eventStreamSubscription = diagnosticClient.EventsStreamed.Subscribe(evt =>
            HandleStreamedEventsArrived(diagnosticClient, eventSetSubscription!, eventStreamSubscription!, evt));
        eventSetSubscription = diagnosticClient.EventsSet.Subscribe(events =>
            HandleInitialEventsArrived(diagnosticClient, eventSetSubscription!, eventStreamSubscription!, events));
        _eventSubscriptionOwnerClient = diagnosticClient;
        _eventSetSubscription = eventSetSubscription;
        _eventStreamSubscription = eventStreamSubscription;
        RunDetached(() => diagnosticClient.SubscribeEvents(),
            ex => HandleSubscribeEventsFailure(diagnosticClient, eventSetSubscription, eventStreamSubscription, ex));
    }

    private void StopDiagClientEvents(IDiagnosticClient? diagnosticClientToUnsubscribe = null)
    {
        IDisposable? eventSetSubscription;
        IDisposable? eventStreamSubscription;
        IDiagnosticClient? diagnosticClient;
        lock (_startStopLock)
        {
            if (_eventSetSubscription == null && _eventStreamSubscription == null)
                return;

            eventSetSubscription = _eventSetSubscription;
            eventStreamSubscription = _eventStreamSubscription;
            diagnosticClient = diagnosticClientToUnsubscribe ?? _eventSubscriptionOwnerClient;
            _streamingStarted = false;
            _eventSubscriptionOwnerClient = null;
            _eventSetSubscription = null;
            _eventStreamSubscription = null;
            _eventSubscriptionStopInProgress = diagnosticClient != null;
            _eventSubscriptionStopClient = diagnosticClient;
        }

        eventSetSubscription?.Dispose();
        eventStreamSubscription?.Dispose();

        if (diagnosticClient != null)
            RunDetached(
                () => diagnosticClient.UnsubscribeEvents(),
                ex => HandleUnsubscribeEventsCompletion(diagnosticClient, ex),
                () => HandleUnsubscribeEventsCompletion(diagnosticClient, null));
    }

  
    private void RunDetached(Func<Task> action, Action<Exception>? onError = null, Action? onSuccess = null)
    {
        try
        {
            Task task = action();
            if (task.IsCompletedSuccessfully)
            {
                onSuccess?.Invoke();
                return;
            }

            _ = ObserveDetachedTask(task, onError, onSuccess);
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
        }
    }

    private async Task ObserveDetachedTask(Task task, Action<Exception>? onError, Action? onSuccess)
    {
        try
        {
            await task;
            onSuccess?.Invoke();
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
        }
    }

    private void HandleSubscribeEventsFailure(
        IDiagnosticClient diagnosticClient,
        IDisposable eventSetSubscription,
        IDisposable eventStreamSubscription,
        Exception ex)
    {
        lock (_startStopLock)
        {
            if (!MatchesCurrentEventSubscriptions(diagnosticClient, eventSetSubscription, eventStreamSubscription))
                return;

            eventSetSubscription.Dispose();
            eventStreamSubscription.Dispose();
            _eventSubscriptionOwnerClient = null;
            _eventSetSubscription = null;
            _eventStreamSubscription = null;
            _streamingStarted = false;
        }

        Trace.WriteLine($"DiagnosticSubscription {Process.Id} failed to subscribe events: {ex.Message}");

        // F-M16: Schedule a retry after a 5-second delay to recover from transient failures
        Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => StartIfRequired(), TaskScheduler.Default);
    }

    private void HandleUnsubscribeEventsCompletion(IDiagnosticClient diagnosticClient, Exception? ex)
    {
        lock (_startStopLock)
        {
            if (!_eventSubscriptionStopInProgress || !ReferenceEquals(_eventSubscriptionStopClient, diagnosticClient))
                return;

            _eventSubscriptionStopInProgress = false;
            _eventSubscriptionStopClient = null;

            if (!_eventSubscriptionRestartBlocked
                && _webClients.Any() && DiagnosticClient != null && _eventStreamSubscription == null)
                StartDiagClientEvents();
        }

        if (ex != null)
            Trace.WriteLine($"DiagnosticSubscription {Process.Id} failed to unsubscribe events: {ex.Message}");
    }

    private bool MatchesCurrentEventSubscriptions(
        IDiagnosticClient diagnosticClient,
        IDisposable eventSetSubscription,
        IDisposable eventStreamSubscription)
    {
        return ReferenceEquals(DiagnosticClient, diagnosticClient)
            && ReferenceEquals(_eventSetSubscription, eventSetSubscription)
            && ReferenceEquals(_eventStreamSubscription, eventStreamSubscription);
    }

    private void HandleInitialEventsArrived(
        IDiagnosticClient diagnosticClient,
        IDisposable eventSetSubscription,
        IDisposable eventStreamSubscription,
        SystemEvent[] events)
    {
        //Debug.WriteLine($"@@@@@@@@@@ DiagnosticSubscription {_instance} initial events arrived {events.Length}");

        lock (_startStopLock)
        {
            if (!MatchesCurrentEventSubscriptions(diagnosticClient, eventSetSubscription, eventStreamSubscription))
                return;

            if (_streamingStarted)
                return;

            _eventRepo.LogEvents(events);
            _streamingStarted = true;

            foreach (WebClientHandler handler in _webClients.Values)
                handler.StartStreamingEvents(Process.Id, _eventRepo);
        }
    }

    private void HandleStreamedEventsArrived(
        IDiagnosticClient diagnosticClient,
        IDisposable eventSetSubscription,
        IDisposable eventStreamSubscription,
        SystemEvent[] events)
    {
        lock (_startStopLock)
        {
            if (!MatchesCurrentEventSubscriptions(diagnosticClient, eventSetSubscription, eventStreamSubscription))
                return;

            //Debug.WriteLine($"@@@@@@@@@@ DiagnosticSubscription {_instance} received single event {events.FirstOrDefault()?.Id}");
            _eventRepo.LogEvents(events);
        }
    }

    private void StopIfRequired()
    {
        lock (_startStopLock)
        {
            if (_webClients.Count == 0 && _requestLoop != null)
                StopRequestLoop();

            if (_webClients.Count == 0 && _eventStreamSubscription != null)
                StopDiagClientEvents();
        }
    }


    private async Task RunLoop(IDiagnosticClient client, CancellationToken cancelToken)
    {
        //Debug.WriteLine($"@@@@@@@@@@ RunLoop {Process.Id} enter");
        try
        {
            while (!cancelToken.IsCancellationRequested)
            {
                try
                {
                    if (client != null)
                    {
                        DiagnosticResponse diags = await client.GetDiagnostics(cancelToken);
                        // A cancelled (superseded) loop must not publish stale results or push to
                        // clients — otherwise a client swap briefly runs two loops racing _lastResponse.
                        if (cancelToken.IsCancellationRequested)
                            break;
                        _lastResponse = diags;
                        //Debug.WriteLine($"@@@@@@@@@@ RunLoop got diags {Process.Id} {diags}");
                        await Task.WhenAll(_webClients.Values.Select(client => TrySend(client, diags)));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await Task.WhenAll(_webClients.Values.Select(client => TrySendError(client, ex.Message)));
                    //Debug.WriteLine($"@@@@@@@@@@ RunLoop {Process.Id} exception {ex.Message}");
                }

                await Task.Delay(2000, cancelToken);
            }
        }
        catch (TaskCanceledException) {}
        catch (OperationCanceledException) {}

        //Debug.WriteLine($"@@@@@@@@@@ RunLoop {Process.Id} exit");
    }

    private async Task TrySend(WebClientHandler client, DiagnosticResponse diags)
    {
        try
        {
            await client.ShowDiagnostics(Process.Id, diags);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"@@@@@@@@@@ RunLoop {Process.Id} TrySend fail {ex.Message}");
        }
    }

    private async Task TrySendError(WebClientHandler client, string message)
    {
        try
        {
            await client.ShowDiagnosticsError(Process.Id, message);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"@@@@@@@@@@ RunLoop {Process.Id} TrySendError fail {ex.Message}");
        }
    }
}
