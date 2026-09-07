using AwesomeAssertions;
using Diagnostic.Service.ClientHandlers;
using DiagnosticExplorer.Logging;
using Microsoft.Extensions.Time.Testing;
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

        var replaced = store.MergeInitialization(Initialization("stream-2", Event("stream-2", 1)));

        replaced.Should().BeTrue();
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

    /// <summary>Retention also drops by age, measured on the injected clock rather than the wall.</summary>
    [Fact]
    public void Append_WithEventsOlderThanTheAgeLimit_DropsThem()
    {
        FakeTimeProvider clock = new(Now);
        LogEventRelayStore store = new(clock);
        var initialization = Initialization("stream-1");
        initialization.MaxAgeMinutes = 10;
        store.MergeInitialization(initialization);

        store.Append([Event("stream-1", 1, Now.AddMinutes(-30)), Event("stream-1", 2, Now.AddMinutes(-1))]);

        store.CreateInitialization().ReplayEvents.Select(e => e.Sequence).Should().Equal(2);
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

    private static LogEventRelayStore CreateStore() => new(new FakeTimeProvider(Now));

    private static LogStreamInitialization Initialization(string streamId, params LogStreamEvent[] replay)
    {
        return new LogStreamInitialization
        {
            StreamId = streamId,
            Routing = new LogStreamRoutingConfiguration(),
            ReplayEvents = replay,
            HighWatermark = 0,
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
}
