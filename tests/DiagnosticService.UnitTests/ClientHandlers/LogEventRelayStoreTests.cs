using AwesomeAssertions;
using Diagnostic.Service.ClientHandlers;
using DiagnosticExplorer.Logging;
using Xunit;

namespace DiagnosticService.UnitTests.ClientHandlers;

/// <summary>
///     The relay store is what lets a browser attach late, or reload, and still see the history —
///     and what stops an agent's reconnect replay showing every event twice.
/// </summary>
public sealed class LogEventRelayStoreTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    ///     The point of keying on sequence: an agent that reconnects replays what it still holds,
    ///     and the store must recognise those rather than duplicate them.
    /// </summary>
    [Fact]
    public void Append_WithEventsAlreadySeen_ReturnsOnlyTheNewOnes()
    {
        var store = CreateStore();
        store.MergeInitialization(Initialization("stream-1"));
        store.Append([Event("stream-1", 1), Event("stream-1", 2)]);

        var added = store.Append([Event("stream-1", 2), Event("stream-1", 3)]);

        added.Select(e => e.Sequence).Should().Equal(3);
        store.CreateInitialization().ReplayEvents.Select(e => e.Sequence).Should().Equal(1, 2, 3);
    }

    /// <summary>A replay arriving through an initialization is deduplicated the same way.</summary>
    [Fact]
    public void MergeInitialization_ReplayingHeldEvents_DoesNotDuplicateThem()
    {
        var store = CreateStore();
        store.MergeInitialization(Initialization("stream-1"));
        store.Append([Event("stream-1", 1), Event("stream-1", 2)]);

        store.MergeInitialization(Initialization("stream-1", Event("stream-1", 2), Event("stream-1", 3)));

        store.CreateInitialization().ReplayEvents.Select(e => e.Sequence).Should().Equal(1, 2, 3);
    }

    /// <summary>
    ///     A different stream id means the agent process restarted, so its sequence numbers start
    ///     again and mean something else. Keeping the old events would interleave two histories.
    /// </summary>
    [Fact]
    public void MergeInitialization_WithADifferentStreamId_DiscardsTheOldHistory()
    {
        var store = CreateStore();
        store.MergeInitialization(Initialization("stream-1"));
        store.Append([Event("stream-1", 1), Event("stream-1", 2)]);

        store.MergeInitialization(Initialization("stream-2", Event("stream-2", 1)));

        var snapshot = store.CreateInitialization();
        snapshot.StreamId.Should().Be("stream-2");
        snapshot.ReplayEvents.Select(e => e.Sequence).Should().Equal(1);
    }

    /// <summary>An event belonging to another stream is not admitted by the back door.</summary>
    [Fact]
    public void Append_WithAnEventFromAnotherStream_IgnoresIt()
    {
        var store = CreateStore();
        store.MergeInitialization(Initialization("stream-1"));

        var added = store.Append([Event("stream-2", 1)]);

        added.Should().BeEmpty();
    }

    /// <summary>
    ///     Events before any initialization have no stream to be keyed against, and the
    ///     initialization that follows carries the agent's own replay of them.
    /// </summary>
    [Fact]
    public void Append_BeforeAnyInitialization_KeepsNothing()
    {
        var store = CreateStore();

        var added = store.Append([Event("stream-1", 1)]);

        added.Should().BeEmpty();
        store.CreateInitialization().ReplayEvents.Should().BeEmpty();
    }

    [Fact]
    public void MergeInitialization_WithoutAStreamId_Throws()
    {
        var store = CreateStore();

        var act = () => store.MergeInitialization(Initialization(" "));

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>Retention drops the oldest by sequence once the cap is passed.</summary>
    [Fact]
    public void Append_PastTheEventCap_KeepsTheNewest()
    {
        var store = CreateStore();
        var initialization = Initialization("stream-1");
        initialization.MaxEvents = 2;
        store.MergeInitialization(initialization);

        store.Append([Event("stream-1", 1), Event("stream-1", 2), Event("stream-1", 3)]);

        store.CreateInitialization().ReplayEvents.Select(e => e.Sequence).Should().Equal(2, 3);
    }

    /// <summary>
    ///     Retention drops by age, measured from the newest event held rather than a clock.
    /// </summary>
    [Fact]
    public void Append_WithEventsOlderThanTheAgeLimit_DropsThem()
    {
        var store = CreateStore();
        var initialization = Initialization("stream-1");
        initialization.MaxAgeMinutes = 10;
        store.MergeInitialization(initialization);

        store.Append([Event("stream-1", 1, Now.AddMinutes(-30)), Event("stream-1", 2, Now.AddMinutes(-1))]);

        store.CreateInitialization().ReplayEvents.Select(e => e.Sequence).Should().Equal(2);
    }

    /// <summary>
    ///     Age is relative to the stream, not to this service's wall clock.
    /// </summary>
    /// <remarks>
    ///     The timestamps are stamped by the agent. Measuring their age against the service's clock
    ///     would make retention depend on the skew between two machines: a service running a few
    ///     minutes ahead would age out every event as it arrived, so a browser attaching later got
    ///     an empty replay while a browser already watching saw the same events live. Every event
    ///     here is hours away from any plausible "now", and all of them must survive.
    /// </remarks>
    [Fact]
    public void Append_WithEventsFarFromTheServiceClock_KeepsThemAnyway()
    {
        var store = CreateStore();
        var initialization = Initialization("stream-1");
        initialization.MaxAgeMinutes = 10;
        store.MergeInitialization(initialization);

        var agentTime = Now.AddDays(-3);
        store.Append([Event("stream-1", 1, agentTime), Event("stream-1", 2, agentTime.AddMinutes(1))]);

        store.CreateInitialization().ReplayEvents.Select(e => e.Sequence).Should().Equal(1, 2);
    }

    /// <summary>
    ///     A late initialization carrying an older position must NOT wipe the history.
    /// </summary>
    /// <remarks>
    ///     An agent's previous send loop can still have an initialization in flight when a new one
    ///     starts, and the hub runs several invocations per connection concurrently, so the older
    ///     one can land second with an older watermark. Treating that as a restarted stream clears
    ///     the relay and hands every browser an empty snapshot — and because an initialization no
    ///     longer carries its own replay, the wipe sticks until the next subscribe cycle. The
    ///     stream id is the only restart signal for exactly this reason.
    /// </remarks>
    [Fact]
    public void MergeInitialization_WithAStaleWatermarkForTheSameStream_KeepsTheHistory()
    {
        var store = CreateStore();
        store.MergeInitialization(Initialization("stream-1"));
        store.Append([Event("stream-1", 40), Event("stream-1", 41)]);

        var stale = Initialization("stream-1", Event("stream-1", 1));
        stale.HighWatermark = 1;
        store.MergeInitialization(stale);

        store.CreateInitialization().ReplayEvents.Select(e => e.Sequence).Should().Equal(1, 40, 41);
    }

    /// <summary>
    ///     An ordinary reconnect on the same stream merges rather than replacing.
    /// </summary>
    [Fact]
    public void MergeInitialization_WithTheSameStreamIdAndAHigherWatermark_Merges()
    {
        var store = CreateStore();
        store.MergeInitialization(Initialization("stream-1"));
        store.Append([Event("stream-1", 1)]);

        var reconnected = Initialization("stream-1", Event("stream-1", 2));
        reconnected.HighWatermark = 2;
        store.MergeInitialization(reconnected);

        store.CreateInitialization().ReplayEvents.Select(e => e.Sequence).Should().Equal(1, 2);
    }

    /// <summary>The high watermark tracks the furthest sequence seen, however it arrived.</summary>
    [Fact]
    public void CreateInitialization_ReportsTheHighestSequenceSeen()
    {
        var store = CreateStore();
        store.MergeInitialization(Initialization("stream-1"));

        store.Append([Event("stream-1", 7)]);

        store.CreateInitialization().HighWatermark.Should().Be(7);
    }

    /// <summary>
    ///     A malformed retention figure from an agent must not take the process down.
    /// </summary>
    /// <remarks>
    ///     MaxAgeMinutes arrives over the wire. Prune does TimeSpan.FromMinutes on it, which throws
    ///     past TimeSpan's range — and it throws AFTER the value has been stored, so every later
    ///     append, merge and snapshot for that process throws too, and AddWebClient faults inside
    ///     the subscription lock. One bad number would make a process unwatchable until restart.
    /// </remarks>
    [Theory]
    [InlineData(double.MaxValue)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NaN)]
    [InlineData(-1d)]
    public void MergeInitialization_WithAnUnusableMaxAge_FallsBackInsteadOfPoisoningTheStore(double maxAgeMinutes)
    {
        var store = CreateStore();
        var initialization = Initialization("stream-1");
        initialization.MaxAgeMinutes = maxAgeMinutes;
        store.MergeInitialization(initialization);
        var act = () => store.Append([Event("stream-1", 1)]);
        act.Should().NotThrow();
        store.CreateInitialization().MaxAgeMinutes.Should().Be(LogEventRetentionOptions.DefaultMaxAgeMinutes);
    }

    private static LogEventRelayStore CreateStore() => new();

    private static LogStreamInitialization Initialization(string streamId, params LogStreamEvent[] replay)
    {
        return new LogStreamInitialization
        {
            StreamId = streamId,
            Routing = new LogStreamRoutingConfiguration(),
            ReplayEvents = replay,
            // As a real agent reports it: the furthest sequence it has published. A test that needs
            // a specific watermark sets it explicitly.
            HighWatermark = replay.Length == 0 ? 0 : replay.Max(e => e.Sequence),
            MaxEvents = 100,
            MaxAgeMinutes = 60,
        };
    }

    private static LogStreamEvent Event(string streamId, long sequence, DateTime? timestampUtc = null)
    {
        return new LogStreamEvent
        {
            StreamId = streamId,
            Sequence = sequence,
            TimestampUtc = timestampUtc ?? Now,
            LoggerCategory = "App",
            Level = 2,
            Message = $"event-{sequence}",
        };
    }

    /// <summary>
    ///     A stale initialization must not put an older routing snapshot back in force.
    /// </summary>
    /// <remarks>
    ///     Events are keyed by sequence so a late one is simply dropped, but routing and retention
    ///     are last-write-wins, and routing is what the browser places every event with.
    /// </remarks>
    [Fact]
    public void MergeInitialization_WithAStaleWatermark_KeepsTheNewerRouting()
    {
        var store = CreateStore();
        var current = Initialization("stream-1");
        current.HighWatermark = 40;
        current.Routing = new LogStreamRoutingConfiguration { MatchMode = EventSinkRouteMatchMode.FirstMatch };
        store.MergeInitialization(current);

        var stale = Initialization("stream-1");
        stale.HighWatermark = 1;
        stale.Routing = new LogStreamRoutingConfiguration { MatchMode = EventSinkRouteMatchMode.AllMatches };
        store.MergeInitialization(stale);

        store.CreateInitialization().Routing.MatchMode.Should().Be(EventSinkRouteMatchMode.FirstMatch);
    }
}
