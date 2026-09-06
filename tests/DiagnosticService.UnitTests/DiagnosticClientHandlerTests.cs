using AwesomeAssertions;
using Diagnostic.Service.ClientHandlers;
using Diagnostic.Service.Hubs;
using DiagnosticExplorer;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using Xunit;

namespace DiagnosticService.UnitTests;

public class DiagnosticClientHandlerTests
{
    /// <summary>
    ///     A caller that gives up must be released at once, not held until the agent answers.
    /// </summary>
    /// <remarks>
    ///     SignalR client results fault when the AGENT's connection ends — which is a different
    ///     connection from the caller's. Adopting client results therefore removed the release that
    ///     the old ConnectionAborted registration provided, and this pins it back: the invocation
    ///     here never completes, so the test can only pass if the caller's token is being observed.
    /// </remarks>
    [Fact]
    public async Task GetDiagnostics_WhenTheCallerCancelsMidFlight_StopsWaitingOnTheAgent()
    {
        var callerContext = Substitute.For<HubCallerContext>();
        callerContext.ConnectionId.Returns("connection-1");
        callerContext.ConnectionAborted.Returns(CancellationToken.None);

        var client = Substitute.For<IDiagnosticHubClient>();
        // Never completes: only the caller's cancellation can end the wait.
        client.GetDiagnostics().Returns(new TaskCompletionSource<DiagnosticResponse>().Task);

        using var handler = new DiagnosticClientHandler(callerContext, client);
        using CancellationTokenSource caller = new();

        Task<DiagnosticResponse> pending = handler.GetDiagnostics(caller.Token);
        pending.IsCompleted.Should().BeFalse("the agent has not answered yet");

        await caller.CancelAsync();

        Func<Task> act = async () => await pending;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>An already-cancelled caller never reaches the agent at all.</summary>
    [Fact]
    public async Task GetDiagnostics_WhenTheCallerHasAlreadyCancelled_Throws()
    {
        var callerContext = Substitute.For<HubCallerContext>();
        callerContext.ConnectionId.Returns("connection-1");
        callerContext.ConnectionAborted.Returns(CancellationToken.None);

        var client = Substitute.For<IDiagnosticHubClient>();
        client.GetDiagnostics().Returns(new TaskCompletionSource<DiagnosticResponse>().Task);

        using var handler = new DiagnosticClientHandler(callerContext, client);
        using CancellationTokenSource caller = new();
        await caller.CancelAsync();

        Func<Task> act = async () => await handler.GetDiagnostics(caller.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SetEvents_WhenPublishedConcurrently_DoesNotOverlapObserverCallbacks()
    {
        var handler = CreateHandler();
        using OverlapDetectingObserver<SystemEvent[]> observer = new();

        using var subscription = handler.EventsSet.Subscribe(observer);

        var publishes = StartConcurrentPublishes(
            24,
            handler,
            static (target, index) => target.SetEvents(new[] { new SystemEvent { Message = $"set-{index}" } })
        );

        try
        {
            observer.WaitUntilCallbackEntered(TestContext.Current.CancellationToken);
        }
        finally
        {
            observer.ReleaseCallbacks();
            try
            {
                await Task.WhenAll(publishes);
            }
            finally
            {
                handler.Dispose();
            }
        }

        observer.OverlapDetected.Should().BeFalse();
        observer.SeenValues.Should().HaveCount(24);
    }

    [Fact]
    public async Task StreamEvents_WhenPublishedConcurrently_DoesNotOverlapObserverCallbacks()
    {
        var handler = CreateHandler();
        using OverlapDetectingObserver<SystemEvent[]> observer = new();

        using var subscription = handler.EventsStreamed.Subscribe(observer);

        var publishes = StartConcurrentPublishes(
            24,
            handler,
            static (target, index) => target.StreamEvents(new[] { new SystemEvent { Message = $"stream-{index}" } })
        );

        try
        {
            observer.WaitUntilCallbackEntered(TestContext.Current.CancellationToken);
        }
        finally
        {
            observer.ReleaseCallbacks();
            try
            {
                await Task.WhenAll(publishes);
            }
            finally
            {
                handler.Dispose();
            }
        }

        observer.OverlapDetected.Should().BeFalse();
        observer.SeenValues.Should().HaveCount(24);
    }

    private static DiagnosticClientHandler CreateHandler()
    {
        var callerContext = Substitute.For<HubCallerContext>();
        callerContext.ConnectionId.Returns("connection-1");
        callerContext.ConnectionAborted.Returns(CancellationToken.None);

        var client = Substitute.For<IDiagnosticHubClient>();
        return new DiagnosticClientHandler(callerContext, client);
    }

    private static Task[] StartConcurrentPublishes<TState>(int count, TState state, Action<TState, int> publish)
    {
        ManualResetEventSlim start = new(false);
        var tasks = Enumerable
            .Range(0, count)
            .Select(index =>
                Task.Run(() =>
                {
                    start.Wait();
                    publish(state, index);
                })
            )
            .ToArray();

        start.Set();
        _ = Task.WhenAll(tasks).ContinueWith(_ => start.Dispose(), TaskScheduler.Default);
        return tasks;
    }

    private sealed class OverlapDetectingObserver<T> : IObserver<T>, IDisposable
    {
        private readonly ManualResetEventSlim _callbackEntered = new(false);
        private readonly ManualResetEventSlim _releaseCallbacks = new(false);
        private readonly List<T> _seenValues = [];
        private int _activeNotifications;

        public bool OverlapDetected { get; private set; }
        public IReadOnlyList<T> SeenValues
        {
            get
            {
                lock (_seenValues)
                {
                    return _seenValues.ToArray();
                }
            }
        }

        public void Dispose()
        {
            _callbackEntered.Dispose();
            _releaseCallbacks.Dispose();
        }

        public void OnCompleted() { }

        public void OnError(Exception error) { }

        public void OnNext(T value)
        {
            if (Interlocked.Increment(ref _activeNotifications) > 1)
            {
                OverlapDetected = true;
            }

            try
            {
                _callbackEntered.Set();
                _releaseCallbacks.Wait(TimeSpan.FromSeconds(30));
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

        public void WaitUntilCallbackEntered(CancellationToken cancellationToken)
        {
            _callbackEntered.Wait(cancellationToken);
        }

        public void ReleaseCallbacks()
        {
            _releaseCallbacks.Set();
        }
    }
}
