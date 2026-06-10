using AwesomeAssertions;
using Diagnostic.Service.Hubs;
using Xunit;

namespace DiagnosticService.UnitTests;

/// <summary>
/// AsyncResultBucket coordinates hub request/response pairs by request id. These tests pin the
/// cancellation and timeout split so disconnect-driven cancellation is not misreported as a
/// timeout and genuine timeouts still surface correctly.
/// </summary>
public class AsyncResultBucketTests
{
    /// <summary>
    /// Verifies that a cancelled caller token is propagated as OperationCanceledException rather
    /// than being rewritten as a timeout. This protects disconnect handling and timeout telemetry.
    /// </summary>
    [Fact]
    public async Task GetResult_WhenCanceledWhileWaiting_ThrowsOperationCanceledException()
    {
        AsyncResultBucket bucket = new();
        using CancellationTokenSource cancel = new();
        await cancel.CancelAsync();

        Func<Task> act = async () => await bucket.GetResult<string>("req-1", TimeSpan.FromSeconds(5), cancel.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Verifies that the real timeout path still throws TimeoutException when nothing completes.
    /// This guards the cancellation fix from accidentally swallowing genuine timeouts.
    /// </summary>
    [Fact]
    public async Task GetResult_WhenNoReplyArrives_ThrowsTimeoutException()
    {
        AsyncResultBucket bucket = new();

        Func<Task> act = async () => await bucket.GetResult<string>("req-2", TimeSpan.FromMilliseconds(20), CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
    }
}
