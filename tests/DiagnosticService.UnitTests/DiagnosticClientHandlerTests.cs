using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using DiagnosticExplorer;
using Diagnostics.Service.Common.Hubs;
using DiagWebService.ClientHandlers;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using Xunit;

namespace DiagnosticService.UnitTests;

public class DiagnosticClientHandlerTests
{
    [Fact]
    public void SetEvents_WhenPublishedConcurrently_DoesNotOverlapObserverCallbacks()
    {
        DiagnosticClientHandler handler = CreateHandler();
        OverlapDetectingObserver<SystemEvent[]> observer = new();

        using IDisposable subscription = handler.EventsSet.Subscribe(observer);

        RunConcurrentPublishes(
            count: 24,
            publish: index => handler.SetEvents(new[] { new SystemEvent { Message = $"set-{index}" } }));

        observer.OverlapDetected.Should().BeFalse();
        observer.SeenValues.Should().HaveCount(24);
    }

    [Fact]
    public void StreamEvents_WhenPublishedConcurrently_DoesNotOverlapObserverCallbacks()
    {
        DiagnosticClientHandler handler = CreateHandler();
        OverlapDetectingObserver<SystemEvent[]> observer = new();

        using IDisposable subscription = handler.EventsStreamed.Subscribe(observer);

        RunConcurrentPublishes(
            count: 24,
            publish: index => handler.StreamEvents(new[] { new SystemEvent { Message = $"stream-{index}" } }));

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

    private static void RunConcurrentPublishes(int count, Action<int> publish)
    {
        using ManualResetEventSlim start = new(false);
        Task[] tasks = Enumerable.Range(0, count)
            .Select(index => Task.Run(() =>
            {
                start.Wait();
                publish(index);
            }))
            .ToArray();

        start.Set();
        Task.WhenAll(tasks).GetAwaiter().GetResult();
    }

    private sealed class OverlapDetectingObserver<T> : IObserver<T>
    {
        private readonly List<T> _seenValues = new();
        private int _activeNotifications;

        public bool OverlapDetected { get; private set; }
        public IReadOnlyList<T> SeenValues => _seenValues;

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value)
        {
            if (Interlocked.Increment(ref _activeNotifications) > 1)
                OverlapDetected = true;

            try
            {
                lock (_seenValues)
                    _seenValues.Add(value);

                Thread.Sleep(10);
            }
            finally
            {
                Interlocked.Decrement(ref _activeNotifications);
            }
        }
    }
}
