using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AwesomeAssertions;
using DiagnosticExplorer;
using DiagnosticExplorer.Common;
using Diagnostics.Service.Common.Hubs;
using Diagnostics.Service.Common.Transport;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace DiagnosticService.UnitTests;

/// <summary>
/// Retro search lifetime is subtle because searches are tracked per connection and complete on
/// background tasks. These tests pin the cleanup behavior so disconnects and overlapping searches
/// do not leak entries or remove the wrong active search.
/// </summary>
public class RetroSearchLifecycleTests
{
    /// <summary>
    /// Verifies that finishing the current search removes its tracking entry. This is the core
    /// leak-prevention behavior the manager needs once a search naturally completes.
    /// </summary>
    [Fact]
    public void HandleSearchFinished_WhenSearchIsCurrent_RemovesTrackedEntry()
    {
        RetroManager manager = CreateManager();
        RetroSearchProcess running = CreateSearch(manager, "conn-1", 1);
        SearchMap(manager)["conn-1"] = running;

        InvokeHandleSearchFinished(manager, running);

        SearchMap(manager).Should().NotContainKey("conn-1");
    }

    /// <summary>
    /// Verifies that finishing an older search does not remove a newer replacement search for the
    /// same connection. This protects the restart path from orphaning the active search.
    /// </summary>
    [Fact]
    public void HandleSearchFinished_WhenReplacementSearchExists_KeepsReplacementTracked()
    {
        RetroManager manager = CreateManager();
        RetroSearchProcess original = CreateSearch(manager, "conn-1", 1);
        RetroSearchProcess replacement = CreateSearch(manager, "conn-1", 2);
        SearchMap(manager)["conn-1"] = replacement;

        InvokeHandleSearchFinished(manager, original);

        SearchMap(manager).Should().ContainKey("conn-1");
        SearchMap(manager)["conn-1"].Should().BeSameAs(replacement);
    }

    /// <summary>
    /// Verifies that connection-level cancellation removes the tracked search and trips its token.
    /// This is the cleanup path WebHub disconnect handling needs to call.
    /// </summary>
    [Fact]
    public void CancelConnectionSearch_WhenSearchExists_RemovesItAndCancelsIt()
    {
        RetroManager manager = CreateManager();
        RetroSearchProcess running = CreateSearch(manager, "conn-1", 1);
        SearchMap(manager)["conn-1"] = running;

        manager.CancelConnectionSearch("conn-1");

        SearchMap(manager).Should().NotContainKey("conn-1");
        CancelToken(running).IsCancellationRequested.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that SendResults still raises Finished when the client error callback itself throws.
    /// This prevents stuck search entries when a disconnected client cannot receive the error.
    /// </summary>
    [Fact]
    public async Task SendResults_WhenErrorCallbackThrows_StillRaisesFinished()
    {
        RetroManager manager = CreateManager();
        IWebHubClient client = Substitute.For<IWebHubClient>();
        client.ProcessSearchError(7, Arg.Any<string>(), Arg.Any<string>())
            .Returns(_ => throw new InvalidOperationException("client-gone"));
        RetroSearchProcess process = new(manager, "conn-1", client, new RetroQuery { SearchId = 7 });
        bool finishedRaised = false;
        process.Finished += (_, _) => finishedRaised = true;

        Channel<RetroSearchResult> channel = Channel.CreateUnbounded<RetroSearchResult>();
        channel.Writer.Complete(new InvalidOperationException("search-failed"));

        Func<Task> act = async () => await InvokeSendResults(process, channel);

        await act.Should().ThrowAsync<InvalidOperationException>();
        finishedRaised.Should().BeTrue();
    }

    [Fact]
    public void LogEvents_WhenPublishedConcurrently_DoesNotOverlapObserverCallbacks()
    {
        RetroManager manager = CreateManager();
        OverlapDetectingObserver<IList<DiagnosticMsg>> observer = new();
        ISubject<IList<DiagnosticMsg>> subject = Subject.Synchronize(new Subject<IList<DiagnosticMsg>>());

        SetPrivateField(manager, "_logSubject", subject);
        using IDisposable subscription = subject.Subscribe(observer);

        RunConcurrentPublishes(
            count: 24,
            publish: index => manager.LogEvents(new List<DiagnosticMsg> { new() { Message = $"msg-{index}" } }));

        observer.OverlapDetected.Should().BeFalse();
        observer.SeenValues.Should().Be(24);
    }

    private static RetroManager CreateManager()
    {
        DiagServiceSettings settings = new() { RetroType = "mongo", RetroConnection = "mongodb://unused" };
        return new RetroManager(new TestHostApplicationLifetime(), Options.Create(settings));
    }

    private static RetroSearchProcess CreateSearch(RetroManager manager, string connectionId, int searchId)
    {
        return new RetroSearchProcess(manager, connectionId, Substitute.For<IWebHubClient>(), new RetroQuery { SearchId = searchId });
    }

    private static ConcurrentDictionary<string, RetroSearchProcess> SearchMap(RetroManager manager)
    {
        FieldInfo field = typeof(RetroManager).GetField("_searches", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (ConcurrentDictionary<string, RetroSearchProcess>)field.GetValue(manager)!;
    }

    private static CancellationTokenSource CancelToken(RetroSearchProcess process)
    {
        FieldInfo field = typeof(RetroSearchProcess).GetField("_cancelToken", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (CancellationTokenSource)field.GetValue(process)!;
    }

    private static void InvokeHandleSearchFinished(RetroManager manager, RetroSearchProcess process)
    {
        MethodInfo method = typeof(RetroManager).GetMethod("HandleSearchFinished", BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(manager, [process, EventArgs.Empty]);
    }

    private static async Task InvokeSendResults(RetroSearchProcess process, Channel<RetroSearchResult> channel)
    {
        MethodInfo method = typeof(RetroSearchProcess).GetMethod("SendResults", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Task task = (Task)method.Invoke(process, [channel, CancellationToken.None])!;
        await task;
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(target, value);
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
        private int _activeNotifications;
        private int _seenValues;

        public bool OverlapDetected { get; private set; }
        public int SeenValues => _seenValues;

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
                Interlocked.Increment(ref _seenValues);
                Thread.Sleep(10);
            }
            finally
            {
                Interlocked.Decrement(ref _activeNotifications);
            }
        }
    }

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }
}
