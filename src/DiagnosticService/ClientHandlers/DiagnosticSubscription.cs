using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reactive.Disposables;
using Diagnostic.Service.Common;
using DiagnosticExplorer;
using DiagnosticExplorer.Logging;

namespace Diagnostic.Service.ClientHandlers;

public class DiagnosticSubscription
{
    private readonly LogEventRelayStore _eventStore;
    private readonly object _startStopLock = new();
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, WebClientHandler> _webClients = new();
    private IDisposable? _eventSetSubscription;
    private IDisposable? _eventStreamSubscription;
    private IDiagnosticClient? _eventSubscriptionOwnerClient;
    private bool _eventSubscriptionRestartBlocked;
    private IDiagnosticClient? _eventSubscriptionStopClient;
    private bool _eventSubscriptionStopInProgress;
    private DiagnosticResponse? _lastResponse;
    private Task? _requestLoop;
    private CancellationTokenSource? _requestLoopCancelSource;

    public DiagnosticSubscription(DiagProcess process, TimeProvider timeProvider)
    {
        Process = process;
        _timeProvider = timeProvider;
        _eventStore = new LogEventRelayStore(timeProvider);
    }

    public DiagProcess Process { get; set; }
    public IDiagnosticClient? DiagnosticClient { get; private set; }
    public string ProcessId => Process.Id;

    public void SetDiagnosticClient(IDiagnosticClient? diagClient)
    {
        if (DiagnosticClient != diagClient)
        {
            var previousClient = DiagnosticClient;
            lock (_startStopLock)
            {
                DiagnosticClient = diagClient;
                _eventSubscriptionRestartBlocked = true;
                StopRequestLoop();
            }

            // No matching StopWebClientEvents any more. The web clients keep the history the
            // relay store holds; an agent swapping out is reported to them as a process state
            // change, not by blanking their grid.
            StopDiagClientEvents(previousClient);
            lock (_startStopLock)
            {
                _eventSubscriptionRestartBlocked = false;
            }

            StartIfRequired();
        }
    }

    public async Task AddWebClient(WebClientHandler webClient)
    {
        if (_lastResponse != null)
        {
            await TrySend(webClient, _lastResponse);
        }

        lock (_startStopLock)
        {
            var added = _webClients.TryAdd(webClient.ConnectionId, webClient);

            // Whatever this process's stream holds so far, whether or not an agent is attached
            // right now. A browser that opens a process late, or reloads, starts from the same
            // picture as one that was watching all along.
            if (added)
            {
                webClient.InitializeLogStream(ProcessId, _eventStore.CreateInitialization());
            }

            StartIfRequired();
        }
    }

    public void RemoveWebClient(WebClientHandler webClient)
    {
        if (_webClients.ContainsKey(webClient.ConnectionId))
        {
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
            if (
                _webClients.Any()
                && DiagnosticClient != null
                && _eventStreamSubscription == null
                && !_eventSubscriptionStopInProgress
                && !_eventSubscriptionRestartBlocked
            )
            {
                StartDiagClientEvents();
            }

            if (_webClients.Any() && DiagnosticClient != null && _requestLoop == null)
            {
                StartRequestLoop();
            }
        }
    }

    private void StartRequestLoop()
    {
        CancellationTokenSource cts = new();
        _requestLoopCancelSource = cts;
        var loop = RunLoop(DiagnosticClient!, cts.Token);
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
        var diagnosticClient = DiagnosticClient!;
        SingleAssignmentDisposable eventSetSubscription = new();
        SingleAssignmentDisposable eventStreamSubscription = new();
        eventStreamSubscription.Disposable = diagnosticClient.LogStreamEvents.Subscribe(events =>
            HandleStreamedEventsArrived(diagnosticClient, eventSetSubscription, eventStreamSubscription, events)
        );
        eventSetSubscription.Disposable = diagnosticClient.LogStreamInitialized.Subscribe(initialization =>
            HandleInitialEventsArrived(diagnosticClient, eventSetSubscription, eventStreamSubscription, initialization)
        );
        _eventSubscriptionOwnerClient = diagnosticClient;
        _eventSetSubscription = eventSetSubscription;
        _eventStreamSubscription = eventStreamSubscription;
        RunDetached(
            () => diagnosticClient.SubscribeEvents(),
            ex => HandleSubscribeEventsFailure(diagnosticClient, eventSetSubscription, eventStreamSubscription, ex)
        );
    }

    private void StopDiagClientEvents(IDiagnosticClient? diagnosticClientToUnsubscribe = null)
    {
        IDisposable? eventSetSubscription;
        IDisposable? eventStreamSubscription;
        IDiagnosticClient? diagnosticClient;
        lock (_startStopLock)
        {
            if (_eventSetSubscription == null && _eventStreamSubscription == null)
            {
                return;
            }

            eventSetSubscription = _eventSetSubscription;
            eventStreamSubscription = _eventStreamSubscription;
            diagnosticClient = diagnosticClientToUnsubscribe ?? _eventSubscriptionOwnerClient;
            _eventSubscriptionOwnerClient = null;
            _eventSetSubscription = null;
            _eventStreamSubscription = null;
            _eventSubscriptionStopInProgress = diagnosticClient != null;
            _eventSubscriptionStopClient = diagnosticClient;
        }

        eventSetSubscription?.Dispose();
        eventStreamSubscription?.Dispose();

        if (diagnosticClient != null)
        {
            RunDetached(
                () => diagnosticClient.UnsubscribeEvents(),
                ex => HandleUnsubscribeEventsCompletion(diagnosticClient, ex),
                () => HandleUnsubscribeEventsCompletion(diagnosticClient, null)
            );
        }
    }

    private static void RunDetached(Func<Task> action, Action<Exception>? onError = null, Action? onSuccess = null)
    {
        try
        {
            var task = action();
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

    private static async Task ObserveDetachedTask(Task task, Action<Exception>? onError, Action? onSuccess)
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
        Exception ex
    )
    {
        lock (_startStopLock)
        {
            if (!MatchesCurrentEventSubscriptions(diagnosticClient, eventSetSubscription, eventStreamSubscription))
            {
                return;
            }

            eventSetSubscription.Dispose();
            eventStreamSubscription.Dispose();
            _eventSubscriptionOwnerClient = null;
            _eventSetSubscription = null;
            _eventStreamSubscription = null;
        }

        Trace.TraceError($"DiagnosticSubscription {Process.Id} failed to subscribe events: {ex.Message}");

        // F-M16: Schedule a retry after a 5-second delay to recover from transient failures
        Task.Delay(TimeSpan.FromSeconds(5), _timeProvider, CancellationToken.None)
            .ContinueWith(_ => StartIfRequired(), TaskScheduler.Default);
    }

    private void HandleUnsubscribeEventsCompletion(IDiagnosticClient diagnosticClient, Exception? ex)
    {
        lock (_startStopLock)
        {
            if (!_eventSubscriptionStopInProgress || !ReferenceEquals(_eventSubscriptionStopClient, diagnosticClient))
            {
                return;
            }

            _eventSubscriptionStopInProgress = false;
            _eventSubscriptionStopClient = null;

            if (
                !_eventSubscriptionRestartBlocked
                && _webClients.Any()
                && DiagnosticClient != null
                && _eventStreamSubscription == null
            )
            {
                StartDiagClientEvents();
            }
        }

        if (ex != null)
        {
            Trace.TraceError($"DiagnosticSubscription {Process.Id} failed to unsubscribe events: {ex.Message}");
        }
    }

    private bool MatchesCurrentEventSubscriptions(
        IDiagnosticClient diagnosticClient,
        IDisposable eventSetSubscription,
        IDisposable eventStreamSubscription
    )
    {
        return ReferenceEquals(DiagnosticClient, diagnosticClient)
            && ReferenceEquals(_eventSetSubscription, eventSetSubscription)
            && ReferenceEquals(_eventStreamSubscription, eventStreamSubscription);
    }

    /// <summary>
    ///     An agent sent its stream snapshot: merge it and give every web client the merged view.
    /// </summary>
    /// <remarks>
    ///     Every initialization is merged, not just the first. An agent that reconnects sends a
    ///     fresh one, and that is exactly when the browsers need telling: either the stream id is
    ///     unchanged and the merge fills the gap they missed, or it changed because the process
    ///     restarted and the old history no longer belongs to the sequence numbers now arriving.
    ///     The old first-snapshot-wins flag this replaced would have suppressed both.
    /// </remarks>
    private void HandleInitialEventsArrived(
        IDiagnosticClient diagnosticClient,
        IDisposable eventSetSubscription,
        IDisposable eventStreamSubscription,
        LogStreamInitialization initialization
    )
    {
        lock (_startStopLock)
        {
            if (!MatchesCurrentEventSubscriptions(diagnosticClient, eventSetSubscription, eventStreamSubscription))
            {
                return;
            }

            _eventStore.MergeInitialization(initialization);

            var merged = _eventStore.CreateInitialization();
            foreach (var handler in _webClients.Values)
            {
                handler.InitializeLogStream(Process.Id, merged);
            }
        }
    }

    /// <summary>Relays live events, each exactly once.</summary>
    /// <remarks>
    ///     Append returns only what the store had not already seen, so a reconnect that replays
    ///     events through the initialization does not send them to the browser a second time.
    /// </remarks>
    private void HandleStreamedEventsArrived(
        IDiagnosticClient diagnosticClient,
        IDisposable eventSetSubscription,
        IDisposable eventStreamSubscription,
        LogStreamEvent[] events
    )
    {
        lock (_startStopLock)
        {
            if (!MatchesCurrentEventSubscriptions(diagnosticClient, eventSetSubscription, eventStreamSubscription))
            {
                return;
            }

            var added = _eventStore.Append(events);
            if (added.Length == 0)
            {
                return;
            }

            foreach (var handler in _webClients.Values)
            {
                handler.StreamLogEvents(Process.Id, added);
            }
        }
    }

    private void StopIfRequired()
    {
        lock (_startStopLock)
        {
            if (_webClients.Count == 0 && _requestLoop != null)
            {
                StopRequestLoop();
            }

            if (_webClients.Count == 0 && _eventStreamSubscription != null)
            {
                StopDiagClientEvents();
            }
        }
    }

    private async Task RunLoop(IDiagnosticClient client, CancellationToken cancelToken)
    {
        try
        {
            while (!cancelToken.IsCancellationRequested)
            {
                try
                {
                    if (client != null)
                    {
                        var diags = await client.GetDiagnostics(cancelToken);
                        // A cancelled (superseded) loop must not publish stale results or push to
                        // clients — otherwise a client swap briefly runs two loops racing _lastResponse.
                        if (cancelToken.IsCancellationRequested)
                        {
                            break;
                        }

                        _lastResponse = diags;
                        await Task.WhenAll(_webClients.Values.Select(webClient => TrySend(webClient, diags)));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await Task.WhenAll(_webClients.Values.Select(webClient => TrySendError(webClient, ex.Message)));
                }

                await Task.Delay(2000, cancelToken);
            }
        }
        catch (TaskCanceledException)
        {
            /* expected on cancellation */
        }
        catch (OperationCanceledException)
        {
            /* expected on cancellation */
        }
    }

    private async Task TrySend(WebClientHandler client, DiagnosticResponse diags)
    {
        try
        {
            await client.ShowDiagnostics(Process.Id, diags);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"RunLoop {Process.Id} TrySend failed: {ex.Message}");
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
            Trace.TraceError($"RunLoop {Process.Id} TrySendError failed: {ex.Message}");
        }
    }
}
