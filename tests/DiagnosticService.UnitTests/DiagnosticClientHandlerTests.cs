using AwesomeAssertions;
using Diagnostic.Service.ClientHandlers;
using Diagnostic.Service.Hubs;
using DiagnosticExplorer.Events;
using DiagnosticExplorer.Interface;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using Xunit;

namespace DiagnosticService.UnitTests;

public class DiagnosticClientHandlerTests
{
    [Fact]
    public async Task SetEvents_WhenPublishedConcurrently_DoesNotOverlapObserverCallbacks()
    {
        DiagnosticClientHandler handler = CreateHandler();
        OverlapDetectingObserver<SystemEvent[]> observer = new();

        using IDisposable subscription = handler.EventsSet.Subscribe(observer);

        Task[] publishes = StartConcurrentPublishes(
            count: 24,
            publish: index => handler.SetEvents(new[] { new SystemEvent { Message = $"set-{index}" } }));

        observer.WaitUntilCallbackEntered(TestContext.Current.CancellationToken);
        observer.ReleaseCallbacks();
        await Task.WhenAll(publishes);

        observer.OverlapDetected.Should().BeFalse();
        observer.SeenValues.Should().HaveCount(24);
    }

    [Fact]
    public async Task StreamEvents_WhenPublishedConcurrently_DoesNotOverlapObserverCallbacks()
    {
        DiagnosticClientHandler handler = CreateHandler();
        OverlapDetectingObserver<SystemEvent[]> observer = new();

        using IDisposable subscription = handler.EventsStreamed.Subscribe(observer);

        Task[] publishes = StartConcurrentPublishes(
            count: 24,
            publish: index => handler.StreamEvents(new[] { new SystemEvent { Message = $"stream-{index}" } }));

        observer.WaitUntilCallbackEntered(TestContext.Current.CancellationToken);
        observer.ReleaseCallbacks();
        await Task.WhenAll(publishes);

        observer.OverlapDetected.Should().BeFalse();
        observer.SeenValues.Should().HaveCount(24);
    }

    private static DiagnosticClientHandler CreateHandler()
    {
        HubCallerContext callerContext = Substitute.For<HubCallerContext>();
        callerContext.ConnectionId.Returns("connection-1");
        callerContext.ConnectionAborted.Returns(CancellationToken.None);

        IDiagnosticHubClient client = Substitute.For<IDiagnosticHubClient>();
        return new DiagnosticClientHandler(callerContext, client, new AsyncResultBucket());
    }

    private static Task[] StartConcurrentPublishes(int count, Action<int> publish)
    {
        ManualResetEventSlim start = new(false);
        Task[] tasks = Enumerable.Range(0, count)
            .Select(index => Task.Run(() => {
                start.Wait();
                publish(index);
            }))
            .ToArray();

        start.Set();
        return tasks;
    }

    private sealed class OverlapDetectingObserver<T> : IObserver<T>
    {
        private readonly ManualResetEventSlim _callbackEntered = new(false);
        private readonly ManualResetEventSlim _releaseCallbacks = new(false);
        private readonly List<T> _seenValues = [];
        private int _activeNotifications;

        public bool OverlapDetected { get; private set; }
        public IReadOnlyList<T> SeenValues => _seenValues;

        public void WaitUntilCallbackEntered(CancellationToken cancellationToken)
        {
            _callbackEntered.Wait(cancellationToken);
        }

        public void ReleaseCallbacks()
        {
            _releaseCallbacks.Set();
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value)
        {
            if (Interlocked.Increment(ref _activeNotifications) > 1)
            {
                OverlapDetected = true;
            }

            try
            {
                _callbackEntered.Set();
                _releaseCallbacks.Wait();
                lock (_seenValues)
                {
                    _seenValues.Add(value);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeNotifications);
            }
        }
    }
}
