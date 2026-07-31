using System.Reflection;
using AwesomeAssertions;
using Diagnostic.Service.Common;
using Diagnostic.Service.Hubs;
using DiagnosticExplorer;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace DiagnosticService.UnitTests;

/// <summary>
///     RetroManager's retry loop abandons a batch after exactly 10 attempts, and the finally block
///     at RetroManager.cs:172-178 decrements _writeQueueSize whether the batch was written or
///     abandoned. That decrement is the only thing stopping a permanently-failing logger from
///     leaking WriteQueueSize upward forever (incident comment (A8)). Finding (DE-10).
/// </summary>
public class RetroManagerRetryTests
{
    /// <summary>
    ///     Verifies that a permanently-failing logger is retried exactly 10 times before the batch
    ///     is abandoned, and that WriteQueueSize returns to its pre-enqueue baseline afterwards.
    ///     The ~10s runtime is the production retry behaviour (10 x 1s Task.Delay) being exercised
    ///     — there is no TimeProvider seam on this path, so this is one [Fact], not a theory.
    ///     The substitute logger is set directly (no StartAsync): TryLog only touches _logger and
    ///     _writeQueueSize, so bypassing startup keeps the Rx/channel pipeline out of the test.
    /// </summary>
    [Fact]
    public async Task TryLog_WhenLoggerPermanentlyFails_AbandonsBatchAfter10AttemptsAndRestoresQueueSize()
    {
        RetroManager manager = CreateManager();
        var failingLogger = Substitute.For<IRetroLogger>();
        failingLogger
            .WriteMessages(Arg.Any<ICollection<DiagnosticMsg>>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("logger-down"));
        SetPrivateField(manager, "_logger", failingLogger);

        // Simulate one enqueued batch on top of an existing backlog baseline, as the StartAsync
        // Rx subscription would have counted it before TryLog ran.
        const long baseline = 3;
        DiagnosticMsg[] messages = [new() { Message = "a" }, new() { Message = "b" }];
        SetPrivateField(manager, "_writeQueueSize", baseline + messages.Length);

        await InvokeTryLog(manager, messages);

        await failingLogger
            .Received(10)
            .WriteMessages(Arg.Any<ICollection<DiagnosticMsg>>(), Arg.Any<CancellationToken>());
        manager.WriteQueueSize.Should().Be(baseline);
    }

    /// <summary>
    ///     A batch dropped because the bounded write channel is full must NOT be counted in
    ///     WriteQueueSize. With BoundedChannelFullMode.DropWrite, TryWrite returns true even when
    ///     it drops the batch, so the Rx subscription counted batches that would never reach the
    ///     reader — and only the reader's finally ever decrements — leaking WriteQueueSize upward
    ///     for the whole outage (the same symptom the (A8) comment records fixing). The reader is
    ///     parked inside a gated WriteMessages holding batch 1, so nothing drains while the test
    ///     floods the 10,000-batch channel with 10,050 batches and asserts the 50 dropped batches
    ///     were not counted.
    /// </summary>
    [Fact]
    public async Task LogEvents_WhenChannelIsFull_DroppedBatchesDoNotInflateWriteQueueSize()
    {
        RetroManager manager = CreateManager();
        var writeEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseWrites = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var gatedLogger = Substitute.For<IRetroLogger>();
        gatedLogger
            .WriteMessages(Arg.Any<ICollection<DiagnosticMsg>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                writeEntered.TrySetResult();
                return releaseWrites.Task;
            });

        await manager.StartAsync(TestContext.Current.CancellationToken);
        SetPrivateField(manager, "_logger", gatedLogger);

        try
        {
            // Batch 1 (exactly 50 messages — one count-triggered buffer emission) is picked up
            // by the reader and held inside the gated WriteMessages.
            manager.LogEvents(FloodChunk(50));
            await writeEntered.Task.WaitAsync(GuardTimeout, TestContext.Current.CancellationToken);

            // Each 500-message call forms exactly ten 50-message batches (count-triggered, so
            // the 1-second buffer timer only ever flushes an empty, filtered-out buffer between
            // calls). 1,005 calls = 10,050 batches against a 10,000-batch channel.
            for (var i = 0; i < 1_005; i++)
            {
                manager.LogEvents(FloodChunk(500));
            }

            // 50 in flight + 10,000 x 50 queued. The 50 dropped batches must not be counted.
            manager.WriteQueueSize.Should().Be(50 + 10_000 * 50);
        }
        finally
        {
            releaseWrites.TrySetResult();
            await manager.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    private static readonly TimeSpan GuardTimeout = TimeSpan.FromSeconds(10);

    private static DiagnosticMsg[] FloodChunk(int count)
    {
        var chunk = new DiagnosticMsg[count];
        for (var i = 0; i < count; i++)
        {
            chunk[i] = new DiagnosticMsg { Message = "flood" };
        }

        return chunk;
    }

    private static RetroManager CreateManager()
    {
        DiagServiceSettings settings = new()
        {
            RetroType = "mongo",
            RetroConnection = "mongodb://unused",
        };
        return new RetroManager(Options.Create(settings));
    }

    private static async Task InvokeTryLog(RetroManager manager, IList<DiagnosticMsg> messages)
    {
        var method = typeof(RetroManager).GetMethod(
            "TryLog",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        var task = (Task)method.Invoke(manager, [messages, CancellationToken.None])!;
        await task;
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = target
            .GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(target, value);
    }
}
