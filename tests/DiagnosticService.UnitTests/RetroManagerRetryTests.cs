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
