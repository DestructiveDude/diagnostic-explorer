using AwesomeAssertions;
using Diagnostic.Service.Hubs;
using DiagnosticExplorer.Interface;
using Xunit;

namespace DiagnosticService.UnitTests.Hubs;

/// <summary>
///     <see cref="HubProxyBase.SendRequest{T}" /> is the round-trip RPC every hub call funnels
///     through: it registers a waiter in <see cref="AsyncResultBucket" /> under a fresh request id,
///     sends, then awaits the reply bounded by <see cref="HubProxyBase.Timeout" />. A regression of
///     the timeout to unbounded hangs every RPC to a slow client; a regression to zero fails them
///     all instantly. (DE-21)
/// </summary>
/// <remarks>
///     <see cref="Microsoft.Extensions.Time.Testing.FakeTimeProvider" /> cannot drive this path:
///     <see cref="AsyncResultBucket.GetResult{T}" /> awaits <c>Task.Delay(timeout, cancel)</c>
///     directly with no <see cref="TimeProvider" /> seam. The plumbing is therefore pinned with a
///     short real timeout against a watchdog cancellation token, and the 10-second default is
///     pinned as a value so the literal itself cannot silently change.
/// </remarks>
public sealed class HubProxyBaseTests
{
    /// <summary>
    ///     The default RPC timeout is 10 seconds. Pinning the value: SendRequest only forwards
    ///     whatever Timeout holds, so an edited default would otherwise pass every behavioural test.
    /// </summary>
    [Fact]
    public void DefaultTimeout_IsTenSeconds()
    {
        TestProxy proxy = new(new AsyncResultBucket());

        proxy.ConfiguredTimeout.Should().Be(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    ///     The happy path: the request id handed to the send delegate keys the reply, and the
    ///     awaited result is the value the client posted back into the bucket.
    /// </summary>
    [Fact]
    public async Task SendRequest_WhenReplyArrives_ReturnsResult()
    {
        AsyncResultBucket bucket = new();
        TestProxy proxy = new(bucket);
        string? sentRequestId = null;

        Task<string> pending = proxy.Call<string>(
            requestId =>
            {
                sentRequestId = requestId;
                bucket.SetResult(RpcResult.Success(requestId), "pong");
                return Task.CompletedTask;
            },
            CancellationToken.None
        );

        (await pending).Should().Be("pong");
        sentRequestId.Should().NotBeNull();
    }

    /// <summary>
    ///     When no reply arrives, SendRequest must fault with <see cref="TimeoutException" /> after
    ///     the configured timeout — not hang, and not wait out the 10-second default. The watchdog
    ///     token is what makes a regressed (larger or unbounded) timeout observable: it surfaces as
    ///     OperationCanceledException instead, failing the assertion.
    /// </summary>
    [Fact]
    public async Task SendRequest_WhenNoReplyArrives_TimesOutWithConfiguredTimeout()
    {
        AsyncResultBucket bucket = new();
        TestProxy proxy = new(bucket)
        {
            ConfiguredTimeout = TimeSpan.FromMilliseconds(50),
        };
        using CancellationTokenSource watchdog = new(TimeSpan.FromSeconds(5));

        Func<Task> act = () => proxy.Call<object>(_ => Task.CompletedTask, watchdog.Token);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    /// <summary>
    ///     Test double that exposes the protected SendRequest/Timeout surface without changing it.
    /// </summary>
    private sealed class TestProxy : HubProxyBase
    {
        public TestProxy(AsyncResultBucket responses)
            : base(responses) { }

        public TimeSpan ConfiguredTimeout
        {
            get => Timeout;
            set => Timeout = value;
        }

        public Task<T> Call<T>(Func<string, Task> send, CancellationToken cancel)
        {
            return SendRequest<T>(cancel, send);
        }
    }
}
