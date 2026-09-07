using AwesomeAssertions;
using DiagnosticExplorer.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace DiagnosticExplorer.UnitTests.Logging;

/// <summary>
///     LogEventStore is the replay buffer behind the log stream: it decides what a newly attached
///     client sees and what is thrown away. Both retention limits and the subscriber fan-out are
///     load-bearing, so each is pinned here rather than left to the adapters to discover.
/// </summary>
public class LogEventStoreTests
{
    private static EventSinkLogEvent Event(string category = "App", string message = "m") =>
        new(category, LogLevel.Information, message);

    /// <summary>Sequence numbers are what a client uses to tell replay from live traffic.</summary>
    [Fact]
    public void Publish_AssignsIncrementingSequenceNumbers()
    {
        var store = new LogEventStore();

        var first = store.Publish(Event());
        var second = store.Publish(Event());

        first.Should().Be(1);
        second.Should().Be(2);
    }

    /// <summary>
    ///     The count limit must drop the OLDEST events. Dropping the newest would leave a client
    ///     replaying stale history while the interesting events vanish.
    /// </summary>
    [Fact]
    public void Publish_BeyondMaxEvents_DropsOldestFirst()
    {
        var store = new LogEventStore(new LogEventRetentionOptions().WithMaxEvents(2));

        store.Publish(Event(message: "one"));
        store.Publish(Event(message: "two"));
        store.Publish(Event(message: "three"));

        var replay = store.CreateInitialization().ReplayEvents;

        replay.Select(e => e.Message).Should().Equal("two", "three");
    }

    /// <summary>
    ///     Age-based pruning is why the clock is injected: without it this test would have to
    ///     sleep for the retention window, which is exactly the flaky shape we ban.
    /// </summary>
    [Fact]
    public void Publish_BeyondMaxAge_DropsExpiredEvents()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var store = new LogEventStore(
            new LogEventRetentionOptions().WithMaxAge(TimeSpan.FromMinutes(10)),
            timeProvider: clock
        );

        store.Publish(Event(message: "old"));
        clock.Advance(TimeSpan.FromMinutes(11));
        store.Publish(Event(message: "new"));

        var replay = store.CreateInitialization().ReplayEvents;

        replay.Select(e => e.Message).Should().Equal("new");
    }

    /// <summary>
    ///     A subscriber attaching mid-stream gets the retained events plus the watermark they run
    ///     to, so it can order replay ahead of anything arriving live.
    /// </summary>
    [Fact]
    public void CreateSubscription_CarriesReplayAndHighWatermark()
    {
        var store = new LogEventStore();
        store.Publish(Event(message: "before"));

        using var subscription = store.CreateSubscription();

        subscription.Initialization.Should().NotBeNull();
        subscription.Initialization.HighWatermark.Should().Be(1);
        subscription.Initialization.ReplayEvents.Select(e => e.Message).Should().Equal("before");
        subscription.Initialization.StreamId.Should().Be(store.StreamId);
    }

    /// <summary>Events published after attach must reach the live channel.</summary>
    [Fact]
    public void Publish_AfterSubscribe_DeliversToLiveChannel()
    {
        var store = new LogEventStore();
        using var subscription = store.CreateSubscription();

        store.Publish(Event(message: "live"));

        subscription.Events.TryRead(out var received).Should().BeTrue();
        received!.Message.Should().Be("live");
        received.Sequence.Should().Be(1);
    }

    /// <summary>
    ///     Publishing runs on the caller's logging thread, so a subscriber that stops reading is
    ///     dropped rather than allowed to block it. This pins that deliberate choice: the slow
    ///     subscriber loses its stream, the publisher does not stall.
    /// </summary>
    [Fact]
    public void Publish_WhenSubscriberChannelIsFull_DropsThatSubscriberAndCompletesIt()
    {
        var store = new LogEventStore();
        using var subscription = store.CreateSubscription(liveSubscriptionCapacity: 1);

        store.Publish(Event(message: "fits"));
        store.Publish(Event(message: "overflows"));

        // The event that fit is still delivered; the one that overflowed is lost.
        subscription.Events.TryRead(out var received).Should().BeTrue();
        received!.Message.Should().Be("fits");
        subscription.Events.TryRead(out _).Should().BeFalse();

        // Completion signals only once the writer is completed AND the buffer is drained, so this
        // has to be asserted after the read above, not before it.
        subscription.Events.Completion.IsCompleted.Should().BeTrue();
    }

    /// <summary>A disposed subscription must stop receiving, and must not break publishing.</summary>
    [Fact]
    public void Dispose_DetachesSubscriptionWithoutAffectingPublishing()
    {
        var store = new LogEventStore();
        var subscription = store.CreateSubscription();

        subscription.Dispose();
        var sequence = store.Publish(Event(message: "after-dispose"));

        sequence.Should().Be(1);
        subscription.Events.Completion.IsCompleted.Should().BeTrue();
    }

    /// <summary>
    ///     Retention is cloned on the way in, so a caller that later mutates the options object
    ///     cannot retune a running store from outside its lock.
    /// </summary>
    [Fact]
    public void Configure_DoesNotTrackLaterMutationOfTheCallersOptions()
    {
        var store = new LogEventStore();
        var retention = new LogEventRetentionOptions().WithMaxEvents(2);
        store.Configure(retention, new LogStreamRoutingConfiguration());

        retention.WithMaxEvents(99);
        store.Publish(Event(message: "one"));
        store.Publish(Event(message: "two"));
        store.Publish(Event(message: "three"));

        store.CreateInitialization().ReplayEvents.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(-1, 5)]
    [InlineData(10, 0)]
    [InlineData(10, -1)]
    public void CloneAndValidate_WithNonPositiveLimits_Throws(int maxEvents, double maxAgeMinutes)
    {
        var options = new LogEventRetentionOptions { MaxEvents = maxEvents, MaxAgeMinutes = maxAgeMinutes };

        var act = () => new LogEventStore(options);

        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    ///     Prune calls TimeSpan.FromMinutes(MaxAgeMinutes) on every publish, so a value outside
    ///     TimeSpan's range would throw on the caller's logging thread rather than at startup.
    ///     NaN matters specifically because it survives a `&lt;= 0` guard — every comparison with
    ///     NaN is false — so it has to be rejected explicitly.
    /// </summary>
    [Theory]
    [InlineData(double.MaxValue)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NaN)]
    public void CloneAndValidate_WithOutOfRangeMaxAge_ThrowsAtConstructionNotOnPublish(double maxAgeMinutes)
    {
        var options = new LogEventRetentionOptions { MaxAgeMinutes = maxAgeMinutes };

        var act = () => new LogEventStore(options);

        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    ///     Configure is public and takes a caller-built configuration, so a destination with null
    ///     values must project rather than throw. Clone previously dereferenced these directly
    ///     while the sibling snapshot path did not.
    /// </summary>
    [Fact]
    public void Configure_WithHandBuiltRoutingContainingNulls_DoesNotThrow()
    {
        var store = new LogEventStore();
        var routing = new LogStreamRoutingConfiguration
        {
            Routes =
            [
                new LogStreamRoute
                {
                    LoggerName = "App",
                    Destinations = [new LogStreamRouteDestination { Category = null!, Name = null! }],
                },
            ],
        };

        var act = () => store.Configure(new LogEventRetentionOptions(), routing);

        act.Should().NotThrow();
        store.CreateInitialization().Routing.Routes[0].Destinations[0].Category.Should().NotBeNull();
    }

    /// <summary>A route whose Destinations list is null must not break the projection either.</summary>
    [Fact]
    public void Configure_WithRouteHavingNullDestinationList_DoesNotThrow()
    {
        var store = new LogEventStore();
        var routing = new LogStreamRoutingConfiguration
        {
            Routes = [new LogStreamRoute { LoggerName = "App", Destinations = null! }],
        };

        var act = () => store.Configure(new LogEventRetentionOptions(), routing);

        act.Should().NotThrow();
        store.CreateInitialization().Routing.Routes[0].Destinations.Should().BeEmpty();
    }

    [Fact]
    public void Publish_WithNullEvent_Throws()
    {
        var store = new LogEventStore();

        var act = () => store.Publish(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>An explicit stream id is honoured; an absent one still yields a usable identity.</summary>
    [Fact]
    public void Constructor_WithoutStreamId_GeneratesOne()
    {
        new LogEventStore(streamId: "explicit").StreamId.Should().Be("explicit");
        new LogEventStore().StreamId.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    ///     An oversize Detail is bounded here, not left to fault a wire frame later.
    /// </summary>
    /// <remarks>
    ///     Detail is where a stack trace lands and nothing upstream bounds it. A frame carries up
    ///     to a hundred events against a 10 MB hub receive cap, so one oversize event faults the
    ///     invocation — and because that fault is not cancellation it ends delivery for the
    ///     connection's life, while the event sits in the retained window and is replayed on every
    ///     re-subscribe. Bounding it at publish keeps it out of the window entirely.
    /// </remarks>
    [Fact]
    public void Publish_WithAnOversizeDetail_TruncatesItAndSaysSo()
    {
        var store = new LogEventStore();
        var detail = new string('x', LogEventStore.MaxDetailLength * 2);

        store.Publish(new EventSinkLogEvent("App", LogLevel.Error, "boom", detail));

        var published = store.CreateInitialization().ReplayEvents.Should().ContainSingle().Subject;
        published.Detail!.Length.Should().Be(LogEventStore.MaxDetailLength);
        published.Detail.Should().EndWith("[truncated]");
    }

    [Fact]
    public void Publish_WithAnOversizeMessage_TruncatesIt()
    {
        var store = new LogEventStore();
        var message = new string('x', LogEventStore.MaxMessageLength * 2);

        store.Publish(new EventSinkLogEvent("App", LogLevel.Error, message));

        var published = store.CreateInitialization().ReplayEvents.Should().ContainSingle().Subject;
        published.Message!.Length.Should().Be(LogEventStore.MaxMessageLength);
    }

    [Fact]
    public void Publish_WithTextInsideTheBounds_LeavesItAlone()
    {
        var store = new LogEventStore();

        store.Publish(new EventSinkLogEvent("App", LogLevel.Error, "boom", "detail"));

        var published = store.CreateInitialization().ReplayEvents.Should().ContainSingle().Subject;
        published.Message.Should().Be("boom");
        published.Detail.Should().Be("detail");
    }

    /// <summary>
    ///     A routing change must reach a subscriber that is already streaming.
    /// </summary>
    /// <remarks>
    ///     A subscriber resolves each event's destination from the routing snapshot it was handed
    ///     at subscribe time. Changing the routing without telling it leaves it filing events under
    ///     routes that no longer exist, and silently: an event only a new route admits resolves to
    ///     no destination and is simply not shown. Ending the subscription is the signal — the
    ///     reader's loop already treats completion as "take a fresh one".
    /// </remarks>
    [Fact]
    public async Task ConfigureRouting_WithALiveSubscription_EndsItAsSuperseded()
    {
        var store = new LogEventStore();
        using var subscription = store.CreateSubscription();

        store.ConfigureRouting(new LogStreamRoutingConfiguration { MatchMode = EventSinkRouteMatchMode.FirstMatch });

        await subscription.Events.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        subscription.EndReason.Should().Be(SubscriptionEndReason.Superseded);
        store
            .CreateSubscription()
            .Initialization!.Routing.MatchMode.Should()
            .Be(EventSinkRouteMatchMode.FirstMatch, "a fresh subscription carries the new routing");
    }

    /// <summary>A dropped subscriber is reported as an overrun, which is not routine.</summary>
    [Fact]
    public async Task Publish_WhenASubscriberCannotKeepUp_EndsItAsOverrun()
    {
        var store = new LogEventStore();
        using var subscription = store.CreateSubscription(liveSubscriptionCapacity: 1);

        store.Publish(new EventSinkLogEvent("App", LogLevel.Information, "one"));
        store.Publish(new EventSinkLogEvent("App", LogLevel.Information, "two"));

        // Completion means completed AND drained, so the one queued event has to be taken first.
        _ = await subscription.Events.ReadAsync(TestContext.Current.CancellationToken);

        await subscription.Events.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        subscription.EndReason.Should().Be(SubscriptionEndReason.Overrun);
    }
}
