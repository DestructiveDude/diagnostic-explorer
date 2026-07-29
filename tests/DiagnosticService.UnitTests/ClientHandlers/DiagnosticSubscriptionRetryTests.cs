using System.Reactive.Subjects;
using AwesomeAssertions;
using Diagnostic.Service.ClientHandlers;
using Diagnostic.Service.Common;
using Diagnostic.Service.Hubs;
using DiagnosticExplorer.Events;
using DiagnosticExplorer.Interface;
using NSubstitute;
using Xunit;

namespace DiagnosticService.UnitTests.ClientHandlers;

/// <summary>
/// Tests that <see cref="DiagnosticSubscription"/> recovers from a transient failure
/// in <see cref="IDiagnosticClient.SubscribeEvents"/> by retrying through the public surface.
/// </summary>
public sealed class DiagnosticSubscriptionRetryTests
{
    /// <summary>
    /// When <see cref="IDiagnosticClient.SubscribeEvents"/> fails on the first attempt,
    /// the subscription is retried and eventually succeeds without wall-clock sleeps in the test.
    /// </summary>
    [Fact]
    public async Task SubscribeEvents_FailsOnce_ThenRetriesAndSucceeds()
    {
        FakeDiagnosticClient client = new();
        DiagnosticSubscription subscription = new(
            new DiagProcess { Id = "process-1", ProcessName = "Test Process" }
        );
        IWebHubClient hubClient = Substitute.For<IWebHubClient>();
        WebClientHandler webClient = new("connection-1", hubClient);

        subscription.SetDiagnosticClient(client);
        await subscription.AddWebClient(webClient);

        // The production retry schedules a 5-second delay; gate on the real completion signal
        // rather than sleeping. A generous timeout covers contended CI runners.
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

        await client.SubscribeCompleted.Task.WaitAsync(timeoutCts.Token);

        client.SubscribeCallCount.Should().BeGreaterThanOrEqualTo(2);
    }

    /// <summary>
    /// Minimal fake <see cref="IDiagnosticClient"/> that fails the first subscription attempt
    /// and completes a <see cref="TaskCompletionSource"/> on the retry.
    /// </summary>
    private sealed class FakeDiagnosticClient : IDiagnosticClient
    {
        private readonly Subject<SystemEvent[]> _eventsSet = new();
        private readonly Subject<SystemEvent[]> _eventsStreamed = new();
        private int _subscribeCallCount;

        public TaskCompletionSource SubscribeCompleted { get; } = new();

        public int SubscribeCallCount => _subscribeCallCount;

        public Task<DiagnosticResponse> GetDiagnostics(CancellationToken cancel) =>
            Task.FromResult(new DiagnosticResponse());

        public Task<OperationResponse> SetProperty(string path, string? value) =>
            Task.FromResult(new OperationResponse());

        public Task<OperationResponse> ExecuteOperation(
            string path,
            string operation,
            string[] arguments
        ) => Task.FromResult(new OperationResponse());

        public Task SubscribeEvents()
        {
            int count = Interlocked.Increment(ref _subscribeCallCount);
            if (count == 1)
            {
                return Task.FromException(
                    new InvalidOperationException("Simulated transient subscription failure")
                );
            }

            SubscribeCompleted.TrySetResult();
            return Task.CompletedTask;
        }

        public Task UnsubscribeEvents() => Task.CompletedTask;

        public IObservable<SystemEvent[]> EventsSet => _eventsSet;

        public IObservable<SystemEvent[]> EventsStreamed => _eventsStreamed;
    }
}
