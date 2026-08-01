using System.Reactive.Subjects;
using AwesomeAssertions;
using Diagnostic.Service.ClientHandlers;
using Diagnostic.Service.Common;
using Diagnostic.Service.Hubs;
using DiagnosticExplorer.Events;
using DiagnosticExplorer.Interface;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace DiagnosticService.UnitTests.ClientHandlers;

public sealed class DiagnosticSubscriptionRetryTests
{
    [Fact]
    public async Task FailedSubscription_RetriesAfterConfiguredDelay()
    {
        FakeTimeProvider timeProvider = new();
        FakeDiagnosticClient client = new();
        DiagnosticSubscription subscription = new(
            new DiagProcess { Id = "process-1", ProcessName = "Test Process" },
            timeProvider
        );
        WebClientHandler webClient = new("connection-1", Substitute.For<IWebHubClient>());

        subscription.SetDiagnosticClient(client);
        await subscription.AddWebClient(webClient);
        await client.FirstAttempted.Task.WaitAsync(TestContext.Current.CancellationToken);

        client.SubscribeCallCount.Should().Be(1);

        timeProvider.Advance(TimeSpan.FromSeconds(5));
        await client.RetryCompleted.Task.WaitAsync(TestContext.Current.CancellationToken);

        client.SubscribeCallCount.Should().Be(2);
        subscription.RemoveWebClient(webClient);
    }

    private sealed class FakeDiagnosticClient : IDiagnosticClient
    {
        private readonly Subject<SystemEvent[]> _eventsSet = new();
        private readonly Subject<SystemEvent[]> _eventsStreamed = new();
        private int _subscribeCallCount;

        public TaskCompletionSource FirstAttempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource RetryCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SubscribeCallCount => _subscribeCallCount;

        public Task<DiagnosticResponse> GetDiagnostics(CancellationToken cancel)
        {
            return Task.FromResult(new DiagnosticResponse());
        }

        public Task<OperationResponse> SetProperty(string path, string? value)
        {
            return Task.FromResult(new OperationResponse());
        }

        public Task<OperationResponse> ExecuteOperation(string path, string operation, string[] arguments)
        {
            return Task.FromResult(new OperationResponse());
        }

        public Task SubscribeEvents()
        {
            var attempt = Interlocked.Increment(ref _subscribeCallCount);
            if (attempt == 1)
            {
                FirstAttempted.TrySetResult();
                return Task.FromException(new InvalidOperationException("Transient subscription failure"));
            }

            RetryCompleted.TrySetResult();
            return Task.CompletedTask;
        }

        public Task UnsubscribeEvents()
        {
            return Task.CompletedTask;
        }

        public IObservable<SystemEvent[]> EventsSet => _eventsSet;

        public IObservable<SystemEvent[]> EventsStreamed => _eventsStreamed;
    }
}
