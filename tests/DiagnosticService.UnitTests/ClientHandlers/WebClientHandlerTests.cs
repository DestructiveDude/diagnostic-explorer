using System.Collections.Concurrent;
using System.Diagnostics;
using AwesomeAssertions;
using Diagnostic.Service.ClientHandlers;
using Diagnostic.Service.Common;
using Diagnostic.Service.Hubs;
using DiagnosticExplorer;
using DiagnosticExplorer.Logging;
using Xunit;

namespace DiagnosticService.UnitTests.ClientHandlers;

/// <summary>
///     WebClientHandler serializes per-client SignalR sends onto a single continuation chain
///     so the synchronized subject order is preserved on the wire, and catches per send so
///     one failing send is observed (traced) without breaking the ordering or killing the
///     chain. The log stream depends on that ordering: an initialization that overtook its own
///     live events would discard them. (DE-30)
/// </summary>
public sealed class WebClientHandlerTests
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     The single send chain must deliver process updates to the client in the exact
    ///     order the RealtimeManager raised them. (DE-30)
    /// </summary>
    [Fact]
    public async Task ProcessChanges_AreSentInOrder()
    {
        RealtimeManager manager = new(TimeProvider.System);
        RecordingWebHubClient client = new(expectedUpdates: 5);
        WebClientHandler handler = new("connection-1", client);
        handler.Start(manager);

        DiagProcess[] pushed = Enumerable.Range(0, 5).Select(i => new DiagProcess { Id = $"process-{i}" }).ToArray();
        foreach (DiagProcess process in pushed)
        {
            manager.ProcessChanged.OnNext(process);
        }

        await client.AllUpdatesReceived.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        client.UpdateOrder.Should().Equal(pushed.Select(p => p.Id));
        handler.Stop();
    }

    /// <summary>
    ///     A failing send must be observed (traced) and must not kill the chain: the sends
    ///     after the failure are still attempted, in order. Removing the try/catch in
    ///     EnqueueSend turns this red — the failure is never traced and the exception
    ///     escapes onto the unawaited chain. (DE-30)
    /// </summary>
    [Fact]
    public async Task FailingSend_IsObserved_AndChainContinuesInOrder()
    {
        RealtimeManager manager = new(TimeProvider.System);
        RecordingWebHubClient client = new(expectedUpdates: 2) { FailSetProcesses = true };
        client.FailOnUpdateIds.Add("process-0");
        WebClientHandler handler = new("connection-1", client);
        RecordingTraceListener listener = new();
        Trace.Listeners.Add(listener);
        try
        {
            handler.Start(manager);
            manager.ProcessChanged.OnNext(new DiagProcess { Id = "process-0" });
            manager.ProcessChanged.OnNext(new DiagProcess { Id = "process-1" });

            // The initial SetProcesses failed and process-0's update failed, yet every
            // update must still be attempted, in push order.
            await client.AllUpdatesReceived.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
            client.UpdateOrder.Should().Equal("process-0", "process-1");

            // Both failures were observed on the trace, not thrown away on the chain.
            await listener.FailuresObserved.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
            listener
                .Messages.Should()
                .Contain(m => m.Contains("WebClientHandler connection-1 send failed", StringComparison.Ordinal));
        }
        finally
        {
            Trace.Listeners.Remove(listener);
            handler.Stop();
        }
    }

    /// <summary>
    ///     A stream initialization must reach the client before the events that follow it.
    /// </summary>
    /// <remarks>
    ///     Both are enqueued rather than awaited, because DiagnosticSubscription calls them from
    ///     inside its start/stop lock. That makes the send chain the only thing keeping them in
    ///     order, and the order matters: a client applies an initialization by replacing what it
    ///     holds, so one that arrived after its own live events would throw them away. Publishing
    ///     from several threads at once is what a re-initialising agent and a busy stream actually
    ///     do.
    /// </remarks>
    [Fact]
    public async Task InitializeLogStream_IsSentBeforeTheEventsThatFollowIt()
    {
        RecordingWebHubClient client = new(expectedUpdates: 0);
        WebClientHandler handler = new("connection-1", client);

        const int rounds = 20;
        client.ExpectedSends = rounds * 2;

        await Task.WhenAll(
            Enumerable
                .Range(0, rounds)
                .Select(round =>
                    Task.Run(() =>
                    {
                        handler.InitializeLogStream(
                            "process-1",
                            new LogStreamInitialization { StreamId = $"stream-{round}" }
                        );
                        handler.StreamLogEvents(
                            "process-1",
                            [new LogStreamEvent { StreamId = $"stream-{round}", Sequence = round }]
                        );
                    })
                )
        );

        await client.AllSendsReceived.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        handler.Stop();

        // Whatever order the rounds interleaved in, each round's initialization precedes its own
        // events: a chain that did not preserve order would put at least one of them the wrong way
        // round across twenty rounds.
        var sends = client.SendOrder.ToArray();
        foreach (var round in Enumerable.Range(0, rounds))
        {
            var initializedAt = Array.IndexOf(sends, $"init:stream-{round}");
            var streamedAt = Array.IndexOf(sends, $"events:stream-{round}");
            initializedAt.Should().BeGreaterThanOrEqualTo(0);
            streamedAt
                .Should()
                .BeGreaterThan(initializedAt, "round {0}'s events must follow its own initialization", round);
        }
    }

    private sealed class RecordingWebHubClient : IWebHubClient
    {
        private readonly int _expectedUpdates;
        private readonly ConcurrentQueue<string> _updateOrder = new();
        private readonly ConcurrentQueue<string> _sendOrder = new();
        private int _updatesReceived;
        private int _sendsReceived;

        public RecordingWebHubClient(int expectedUpdates)
        {
            _expectedUpdates = expectedUpdates;
        }

        public HashSet<string> FailOnUpdateIds { get; } = [];
        public bool FailSetProcesses { get; set; }

        public TaskCompletionSource AllUpdatesReceived { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllSendsReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>How many log-stream sends to wait for before completing AllSendsReceived.</summary>
        public int ExpectedSends { get; set; }

        public IReadOnlyCollection<string> UpdateOrder => _updateOrder;
        public IReadOnlyCollection<string> SendOrder => _sendOrder;

        public Task SetProcesses(DiagProcess[] processes)
        {
            if (FailSetProcesses)
            {
                throw new InvalidOperationException("SetProcesses failed");
            }

            return Task.CompletedTask;
        }

        public Task UpdateProcess(DiagProcess processes)
        {
            _updateOrder.Enqueue(processes.Id);
            if (Interlocked.Increment(ref _updatesReceived) == _expectedUpdates)
            {
                AllUpdatesReceived.TrySetResult();
            }

            if (FailOnUpdateIds.Contains(processes.Id))
            {
                throw new InvalidOperationException($"UpdateProcess {processes.Id} failed");
            }

            return Task.CompletedTask;
        }

        public Task InitializeLogStream(string id, LogStreamInitialization initialization)
        {
            RecordSend($"init:{initialization.StreamId}");
            return Task.CompletedTask;
        }

        public Task StreamLogEvents(string id, LogStreamEvent[] events)
        {
            RecordSend($"events:{events[0].StreamId}");
            return Task.CompletedTask;
        }

        private void RecordSend(string entry)
        {
            _sendOrder.Enqueue(entry);
            if (Interlocked.Increment(ref _sendsReceived) == ExpectedSends)
            {
                AllSendsReceived.TrySetResult();
            }
        }

        public Task RemoveProcess(string id) => Task.CompletedTask;

        public Task ShowDiagnostics(string id, DiagnosticResponse response) => Task.CompletedTask;

        public Task ShowDiagnosticsError(string id, string message) => Task.CompletedTask;

        public Task ProcessSearchResults(RetroSearchResult result) => Task.CompletedTask;

        public Task ProcessSearchEnd(int searchId) => Task.CompletedTask;

        public Task ProcessSearchError(int searchId, string message, string detail) => Task.CompletedTask;
    }

    private sealed class RecordingTraceListener : TraceListener
    {
        private readonly ConcurrentQueue<string> _messages = new();
        private int _failuresObserved;

        public TaskCompletionSource FailuresObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyCollection<string> Messages => _messages;

        public override void Write(string? message) { }

        public override void WriteLine(string? message)
        {
            if (message == null)
            {
                return;
            }

            _messages.Enqueue(message);
            if (
                message.Contains("send failed", StringComparison.Ordinal)
                && Interlocked.Increment(ref _failuresObserved) == 2
            )
            {
                FailuresObserved.TrySetResult();
            }
        }
    }
}
