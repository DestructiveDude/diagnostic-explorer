using AwesomeAssertions;
using DiagnosticExplorer.Hosting;

namespace DiagnosticExplorer.UnitTests;

/// <summary>
///     DiagnosticHostingService claims a static single-instance slot via
///     Interlocked.CompareExchange before starting, and must roll the slot back to null when
///     StartHosting fails — otherwise one failed Start permanently wedges every later Start
///     with "already running", and the published slot could hold a half-initialized instance.
///     (DE-31)
/// </summary>
public class DiagnosticHostingServiceTests
{
    /// <summary>
    ///     A Start that fails (no Uri configured) must release the slot, so a later Start
    ///     retries instead of throwing "already running". Deleting the rollback
    ///     CompareExchange in TryStart turns this red: the second Start throws
    ///     InvalidOperationException. (DE-31)
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Start_FailedStart_ReleasesSlotForLaterStart(string? url)
    {
        Action firstStart = () => DiagnosticHostingService.Start(url!);
        Action secondStart = () => DiagnosticHostingService.Start(url!);

        firstStart.Should().NotThrow();
        secondStart.Should().NotThrow();
    }

    /// <summary>
    ///     After a failed Start the slot must read as null (not a half-initialized
    ///     instance), so the static LogEvent path no-ops instead of walking null
    ///     registration handlers. (DE-31)
    /// </summary>
    [Fact]
    public void LogEvent_AfterFailedStart_IsNoOp()
    {
        DiagnosticHostingService.Start("");

        Action act = () => DiagnosticHostingService.LogEvent(new DiagnosticMsg());

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Stop must tolerate a failed Start: the slot is empty, so Stop has nothing to
    ///     tear down and must complete cleanly. (DE-31)
    /// </summary>
    [Fact]
    public async Task Stop_AfterFailedStart_CompletesWithoutError()
    {
        DiagnosticHostingService.Start("");

        Func<Task> act = DiagnosticHostingService.Stop;

        await act.Should().NotThrowAsync();
    }
}
