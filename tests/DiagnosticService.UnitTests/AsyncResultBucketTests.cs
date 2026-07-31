using AwesomeAssertions;
using Diagnostic.Service.Hubs;
using DiagnosticExplorer.Interface;
using Xunit;

namespace DiagnosticService.UnitTests;

/// <summary>
///     AsyncResultBucket coordinates hub request/response pairs by request id. These tests pin the
///     cancellation and timeout split so disconnect-driven cancellation is not misreported as a
///     timeout and genuine timeouts still surface correctly.
/// </summary>
public class AsyncResultBucketTests
{
    /// <summary>
    ///     Verifies that a cancelled caller token is propagated as OperationCanceledException rather
    ///     than being rewritten as a timeout. This protects disconnect handling and timeout telemetry.
    /// </summary>
    [Fact]
    public async Task GetResult_WhenCanceledWhileWaiting_ThrowsOperationCanceledException()
    {
        AsyncResultBucket bucket = new();
        using CancellationTokenSource cancel = new();
        cancel.CancelAfter(50);

        // ReSharper disable once AccessToDisposedClosure -- assertion completes before disposal.
        Func<Task> act = async () =>
            await bucket.GetResult<string>("req-1", TimeSpan.FromSeconds(5), cancel.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    ///     Verifies that the real timeout path still throws TimeoutException when nothing completes.
    ///     This guards the cancellation fix from accidentally swallowing genuine timeouts.
    /// </summary>
    [Fact]
    public async Task GetResult_WhenNoReplyArrives_ThrowsTimeoutException()
    {
        AsyncResultBucket bucket = new();

        Func<Task> act = async () =>
            await bucket.GetResult<string>(
                "req-2",
                TimeSpan.FromMilliseconds(20),
                CancellationToken.None
            );

        await act.Should().ThrowAsync<TimeoutException>();
    }

    /// <summary>
    ///     (DE-13) A duplicate reply for the same request id — possible under
    ///     MaximumParallelInvocationsPerClient, or a late reply racing GetResult's removal — must
    ///     no-op via TrySetResult rather than throwing "already completed" out of the hub
    ///     invocation. The first reply wins. The duplicate is delivered while the waiter is still
    ///     registered (its continuation is parked on a queued SynchronizationContext), so the
    ///     TrySetResult path is exercised deterministically rather than racing GetResult's
    ///     finally-removal.
    /// </summary>
    [Fact]
    public async Task SetResult_WhenReplyArrivesTwice_KeepsFirstResult()
    {
        AsyncResultBucket bucket = new();
        QueuedSynchronizationContext context = new();
        SynchronizationContext? original = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);

        Task<string> pending;
        try
        {
            pending = bucket.GetResult<string>(
                "req-dup",
                TimeSpan.FromMinutes(1),
                CancellationToken.None
            );

            // Complete the waiter while SynchronizationContext.Current is null: a captured-context
            // continuation can otherwise inline on the completing thread, which would run
            // GetResult's finally and remove the waiter before the duplicate arrives. With Current
            // cleared, the continuation is posted to the queued context instead and stays parked.
            SynchronizationContext.SetSynchronizationContext(null);
            bucket.SetResult(RpcResult.Success("req-dup"), "first");
            SynchronizationContext.SetSynchronizationContext(context);

            // Duplicate reply: the waiter is completed but still registered, so this reaches the
            // TrySetResult path and must no-op, not throw.
            bucket.SetResult(RpcResult.Success("req-dup"), "second");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(original);
        }

        context.RunAll();

        string result = await pending;
        result.Should().Be("first");
    }

    /// <summary>
    ///     (DE-13) A reply with no registered waiter — the caller already timed out or cancelled —
    ///     must be logged and discarded, not thrown out of the hub invocation.
    /// </summary>
    [Fact]
    public void SetResult_WhenNoWaiterIsRegistered_DiscardsReply()
    {
        AsyncResultBucket bucket = new();

        Action act = () => bucket.SetResult(RpcResult.Success("req-orphan"), "late");

        act.Should().NotThrow();
    }

    /// <summary>
    ///     A SynchronizationContext that queues posted callbacks instead of running them, so a test
    ///     can hold an awaited continuation parked and pump it explicitly afterwards.
    /// </summary>
    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly object _gate = new();
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _callbacks = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_gate)
            {
                _callbacks.Enqueue((d, state));
            }
        }

        public void RunAll()
        {
            while (true)
            {
                (SendOrPostCallback Callback, object? State) posted;
                lock (_gate)
                {
                    if (_callbacks.Count == 0)
                    {
                        return;
                    }

                    posted = _callbacks.Dequeue();
                }

                posted.Callback(posted.State);
            }
        }
    }
}
