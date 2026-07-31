using System.Collections.Concurrent;
using System.Diagnostics;
using AwesomeAssertions;
using Diagnostic.Service.ClientHandlers;
using Diagnostic.Service.Common;
using Diagnostic.Service.Hubs;
using DiagnosticExplorer.Events;
using DiagnosticExplorer.Interface;
using Xunit;

namespace DiagnosticService.UnitTests.ClientHandlers;

/// <summary>
///     WebClientHandler serializes per-client SignalR sends onto a single continuation chain
///     so the synchronized subject order is preserved on the wire, and catches per send so
///     one failing send is observed (traced) without breaking the ordering or killing the
///     chain. StartStreamingEvents must cancel the stream it replaces, or every event is
///     delivered twice. (DE-30)
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

        DiagProcess[] pushed = Enumerable
            .Range(0, 5)
            .Select(i => new DiagProcess { Id = $"process-{i}" })
            .ToArray();
        foreach (DiagProcess process in pushed)
        {
            manager.ProcessChanged.OnNext(process);
        }

        await client.AllUpdatesReceived.Task.WaitAsync(
            SignalTimeout,
            TestContext.Current.CancellationToken
        );

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
        RecordingWebHubClient client = new(expectedUpdates: 2)
        {
            FailSetProcesses = true,
        };
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
            await client.AllUpdatesReceived.Task.WaitAsync(
                SignalTimeout,
                TestContext.Current.CancellationToken
            );
            client.UpdateOrder.Should().Equal("process-0", "process-1");

            // Both failures were observed on the trace, not thrown away on the chain.
            await listener.FailuresObserved.Task.WaitAsync(
                SignalTimeout,
                TestContext.Current.CancellationToken
            );
            listener
                .Messages.Should()
                .Contain(m =>
                    m.Contains("WebClientHandler connection-1 send failed", StringComparison.Ordinal)
                );
        }
        finally
        {
            Trace.Listeners.Remove(listener);
            handler.Stop();
        }
    }

    /// <summary>
    ///     StartStreamingEvents with an id that already has a stream must cancel the old
    ///     stream before starting the new one. If the old stream is left running, every
    ///     subsequently logged event is delivered to the client twice. Deleting the
    ///     existingState.Cancel.Cancel() call turns this red. (DE-30)
    /// </summary>
    [Fact]
    public async Task StartStreamingEvents_ReplacesStream_EventDeliveredExactlyOnce()
    {
        using EventSinkRepo repo = new();
        RecordingWebHubClient client = new(expectedUpdates: 0);
        WebClientHandler handler = new("connection-1", client);

        handler.StartStreamingEvents("process-1", repo);
        handler.StartStreamingEvents("process-1", repo);

        SystemEvent systemEvent =
            new() { SinkName = "sink", SinkCategory = "category", Message = "hello" };
        repo.LogEvent(systemEvent);

        await client.FirstStreamedBatch.Task.WaitAsync(
            SignalTimeout,
            TestContext.Current.CancellationToken
        );

        // Each stream start pushes its initial snapshot.
        client.SetEventsCallCount.Should().Be(2);

        // Poll with a timeout: the replaced stream was cancelled before the event was
        // logged, so no second delivery can arrive. A zombie stream would have flushed
        // its copy within a couple of its 25ms buffer windows.
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(250);
        while (DateTime.UtcNow < deadline)
        {
            client.StreamedBatchCount.Should().Be(1);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        client.LastStreamedBatch.Should().ContainSingle(e => e.Message == "hello");

        handler.StopStreamingEvents("process-1");
        handler.Stop();
    }

    private sealed class RecordingWebHubClient : IWebHubClient
    {
        private readonly int _expectedUpdates;
        private readonly ConcurrentQueue<string> _updateOrder = new();
        private readonly ConcurrentQueue<SystemEvent> _streamed = new();
        private int _updatesReceived;
        private int _setEventsCallCount;
        private int _streamedBatchCount;

        public RecordingWebHubClient(int expectedUpdates)
        {
            _expectedUpdates = expectedUpdates;
        }

        public HashSet<string> FailOnUpdateIds { get; } = [];
        public bool FailSetProcesses { get; set; }

        public TaskCompletionSource AllUpdatesReceived { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FirstStreamedBatch { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyCollection<string> UpdateOrder => _updateOrder;
        public int SetEventsCallCount => _setEventsCallCount;
        public int StreamedBatchCount => _streamedBatchCount;
        public IReadOnlyCollection<SystemEvent> LastStreamedBatch => _streamed;

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

        public Task SetEvents(string id, SystemEvent[] events)
        {
            Interlocked.Increment(ref _setEventsCallCount);
            return Task.CompletedTask;
        }

        public Task StreamEvents(string id, IList<SystemEvent> evt)
        {
            foreach (SystemEvent systemEvent in evt)
            {
                _streamed.Enqueue(systemEvent);
            }

            Interlocked.Increment(ref _streamedBatchCount);
            FirstStreamedBatch.TrySetResult();
            return Task.CompletedTask;
        }

        public Task RemoveProcess(string id) => Task.CompletedTask;

        public Task ShowDiagnostics(string id, DiagnosticResponse response) =>
            Task.CompletedTask;

        public Task ShowDiagnosticsError(string id, string message) => Task.CompletedTask;

        public Task ProcessSearchResults(RetroSearchResult result) => Task.CompletedTask;

        public Task ProcessSearchEnd(int searchId) => Task.CompletedTask;

        public Task ProcessSearchError(int searchId, string message, string detail) =>
            Task.CompletedTask;
    }

    private sealed class RecordingTraceListener : TraceListener
    {
        private readonly ConcurrentQueue<string> _messages = new();
        private int _failuresObserved;

        public TaskCompletionSource FailuresObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

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
