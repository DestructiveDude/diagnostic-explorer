using AwesomeAssertions;
using DiagnosticExplorer.Log4Net;
using DiagnosticExplorer.Log4Net.Util;

namespace DiagnosticExplorer.UnitTests;

/// <summary>
///     Tests that swap the static <see cref="SystemDateTime" /> clock must not run in
///     parallel with any other test collection.
/// </summary>
[CollectionDefinition("SystemDateTime", DisableParallelization = true)]
public class SystemDateTimeCollection;

/// <summary>
///     The closed/open/half-open circuit breaker in <see cref="AppenderProxyBase.DoAppend" />
///     is the failure-isolation layer that keeps a broken log target from being hammered by
///     the host app. These tests pin the state transitions deterministically via the
///     <see cref="SystemDateTime" /> clock seam — no sleeps. (DE-11)
/// </summary>
[Collection("SystemDateTime")]
public sealed class AppenderProxyCircuitBreakerTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    private readonly Func<DateTime> _originalClock = SystemDateTime.UtcNow;
    private readonly TestProxy _proxy = new(Timeout);
    private DateTime _now = new(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc);

    public AppenderProxyCircuitBreakerTests()
    {
        SystemDateTime.UtcNow = () => _now;
    }

    public void Dispose()
    {
        SystemDateTime.UtcNow = _originalClock;
    }

    /// <summary>
    ///     A failed append opens the breaker; while open, calls are rejected without the
    ///     wrapped append action being invoked at all.
    /// </summary>
    [Fact]
    public void DoAppend_AfterFailure_OpensBreakerAndRejectsWithoutCallingAction()
    {
        var attempts = 0;
        AppendResult Failing()
        {
            attempts++;
            return new AppendResult(false, "target is down");
        }

        _proxy.TryAppend(Failing).Should().BeFalse();
        _proxy.IsInError.Should().BeTrue();

        attempts = 0;
        _proxy.TryAppend(Failing).Should().BeFalse();
        _proxy.TryAppend(Failing).Should().BeFalse();

        attempts.Should().Be(0);
    }

    /// <summary>
    ///     Once the quarantine timeout has elapsed, the next call becomes the half-open
    ///     probe and runs the action; a successful probe closes the breaker and normal
    ///     appends flow again.
    /// </summary>
    [Fact]
    public void DoAppend_AfterCooldown_ProbesOnceAndClosesOnSuccess()
    {
        _proxy.TryAppend(() => new AppendResult(false, "target is down")).Should().BeFalse();

        // Still inside the quarantine window: rejected without probing.
        _now = _now.AddMinutes(1);
        var probeCalls = 0;
        AppendResult Probe()
        {
            probeCalls++;
            return new AppendResult(true);
        }

        _proxy.TryAppend(Probe).Should().BeFalse();
        probeCalls.Should().Be(0);

        // Past the cooldown: this call is the probe and runs the action.
        _now = _now.Add(Timeout);
        _proxy.TryAppend(Probe).Should().BeTrue();
        probeCalls.Should().Be(1);
        _proxy.IsInError.Should().BeFalse();

        // Probe succeeded, breaker closed: subsequent appends go straight through.
        _proxy.TryAppend(Probe).Should().BeTrue();
        probeCalls.Should().Be(2);
    }

    /// <summary>
    ///     DoAppend releases the state lock while the probing action runs, so a reentrant
    ///     TryAppend issued from inside the probe observes exactly what a second thread
    ///     would (half-open) and must be rejected without invoking the wrapped action —
    ///     at most one probe in flight.
    /// </summary>
    [Fact]
    public void DoAppend_WhileHalfOpenProbeInFlight_RejectsReentrantAppend()
    {
        _proxy.TryAppend(() => new AppendResult(false, "target is down")).Should().BeFalse();
        _now = _now.Add(Timeout + TimeSpan.FromSeconds(1));

        bool? reentrantResult = null;
        var reentrantCalls = 0;

        AppendResult Probe()
        {
            reentrantResult = _proxy.TryAppend(() =>
            {
                reentrantCalls++;
                return new AppendResult(true);
            });
            return new AppendResult(true);
        }

        _proxy.TryAppend(Probe).Should().BeTrue();

        reentrantResult.Should().BeFalse();
        reentrantCalls.Should().Be(0);
    }

    /// <summary>
    ///     Minimal concrete proxy exposing the protected DoAppend: the breaker itself is
    ///     entirely real, only the abstract shell is filled in.
    /// </summary>
    private sealed class TestProxy : AppenderProxyBase
    {
        public TestProxy(TimeSpan timeout)
            : base(timeout) { }

        public bool TryAppend(Func<AppendResult> action) => DoAppend(action);
    }
}
