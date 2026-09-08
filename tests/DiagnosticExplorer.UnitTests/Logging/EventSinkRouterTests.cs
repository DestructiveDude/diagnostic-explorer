using System.ComponentModel;
using System.Reflection;
using AwesomeAssertions;
using DiagnosticExplorer.Logging;
using Microsoft.Extensions.Logging;

namespace DiagnosticExplorer.UnitTests.Logging;

/// <summary>
///     EventSinkRouter decides which log events reach the store. Its category matching is the part
///     most likely to be got subtly wrong — a prefix rule that also swallows sibling categories is
///     silent and expensive — so the matching table is pinned exhaustively.
/// </summary>
/// <remarks>
///     Every test passes an explicit store rather than letting the router fall back to
///     <see cref="DiagnosticManager.LogEventStore" />, so nothing here mutates process-wide state
///     except the one test that deliberately toggles <see cref="DiagnosticManager.Enabled" />.
///     Tests within a class run serially, which is what keeps that toggle safe.
/// </remarks>
public class EventSinkRouterTests
{
    private static EventSinkRouteOptions RouteFor(string pattern) =>
        new EventSinkRouteOptions().Route(pattern, route => route.To("Logs", "App"));

    private static EventSinkLogEvent Event(string category, LogLevel level = LogLevel.Information) =>
        new(category, level, "message");

    /// <summary>
    ///     A pattern matches itself and any dotted descendant, but must NOT match a category that
    ///     merely starts with the same letters. "Ems" matching "EmsWidget" would silently capture
    ///     an unrelated subsystem's logs.
    /// </summary>
    [Theory]
    [InlineData("Ems", "Ems", true)]
    [InlineData("Ems", "ems", true)]
    [InlineData("Ems", "Ems.Pricing", true)]
    [InlineData("Ems", "Ems.Pricing.Feed", true)]
    [InlineData("Ems", "EmsWidget", false)]
    [InlineData("Ems", "Other", false)]
    [InlineData("*", "anything.at.all", true)]
    public void IsEnabled_AppliesPrefixMatchingOnDotBoundariesOnly(string pattern, string category, bool expected)
    {
        var router = new EventSinkRouter(RouteFor(pattern), new LogEventStore());

        router.IsEnabled(category, LogLevel.Information).Should().Be(expected);
    }

    [Theory]
    [InlineData(LogLevel.Debug, false)]
    [InlineData(LogLevel.Warning, true)]
    [InlineData(LogLevel.Error, true)]
    [InlineData(LogLevel.Critical, false)]
    public void IsEnabled_RespectsMinimumAndMaximumLevels(LogLevel level, bool expected)
    {
        var options = new EventSinkRouteOptions().Route(
            "App",
            route => route.AtLeast(LogLevel.Warning).AtMost(LogLevel.Error).To("Logs", "App")
        );
        var router = new EventSinkRouter(options, new LogEventStore());

        router.IsEnabled("App", level).Should().Be(expected);
    }

    /// <summary>A matching event is published exactly once; a non-matching one not at all.</summary>
    [Fact]
    public void Route_PublishesOnlyWhenARouteMatches()
    {
        var store = new LogEventStore();
        var router = new EventSinkRouter(RouteFor("App"), store);

        router.Route(Event("App")).Should().Be(1);
        router.Route(Event("Unrelated")).Should().Be(0);

        store.CreateInitialization().ReplayEvents.Should().HaveCount(1);
    }

    /// <summary>
    ///     StopProcessing must halt evaluation at the route that sets it, so a catch-all placed
    ///     after a specific route does not also fire.
    /// </summary>
    [Fact]
    public void Route_WithStopProcessing_DoesNotEvaluateLaterRoutes()
    {
        var options = new EventSinkRouteOptions()
            .Route("App", route => route.To("Logs", "App").StopAfterMatch())
            .Route("*", route => route.To("Logs", "Catchall"));
        var router = new EventSinkRouter(options, new LogEventStore());

        // Both routes would match "App"; the first stops evaluation, so only one destination is live.
        router.IsEnabled("App", LogLevel.Information).Should().BeTrue();
        router.Route(Event("App")).Should().Be(1);
    }

    /// <summary>
    ///     The global kill switch has to be honoured on the publish path, not just at
    ///     configuration time, or disabling diagnostics leaves logs still accumulating.
    /// </summary>
    [Fact]
    public void Route_WhenDiagnosticsAreDisabled_PublishesNothing()
    {
        var store = new LogEventStore();
        var router = new EventSinkRouter(RouteFor("App"), store);

        DiagnosticManager.Enabled = false;
        try
        {
            router.Route(Event("App")).Should().Be(0);
        }
        finally
        {
            DiagnosticManager.Enabled = true;
        }

        store.CreateInitialization().ReplayEvents.Should().BeEmpty();
    }

    /// <summary>
    ///     The router is a filter, not a fan-out: an event matching several routes is published
    ///     exactly once. This is why MatchMode is not consulted when publishing — narrowing the
    ///     matched set could not change the outcome, since only whether it is empty is ever read.
    ///     Pinning it here so a future fan-out change has to break this test deliberately.
    /// </summary>
    [Theory]
    [InlineData(EventSinkRouteMatchMode.AllMatches)]
    [InlineData(EventSinkRouteMatchMode.MostSpecific)]
    [InlineData(EventSinkRouteMatchMode.FirstMatch)]
    public void Route_WhenSeveralRoutesMatch_PublishesExactlyOnceRegardlessOfMatchMode(EventSinkRouteMatchMode mode)
    {
        var store = new LogEventStore();
        var options = new EventSinkRouteOptions()
            .UseMatchMode(mode)
            .Route("Ems", route => route.To("Logs", "Broad"))
            .Route("Ems.Pricing", route => route.To("Logs", "Specific"))
            .Route("*", route => route.To("Logs", "Catchall"));
        var router = new EventSinkRouter(options, store);

        router.Route(Event("Ems.Pricing")).Should().Be(1);

        store.CreateInitialization().ReplayEvents.Should().ContainSingle();
    }

    /// <summary>The configured match mode still reaches the client, which is what renders it.</summary>
    [Fact]
    public void Constructor_CarriesMatchModeIntoTheSnapshot()
    {
        var store = new LogEventStore();
        var options = new EventSinkRouteOptions()
            .UseMatchMode(EventSinkRouteMatchMode.FirstMatch)
            .Route("App", route => route.To("Logs", "App"));

        _ = new EventSinkRouter(options, store);

        store.CreateInitialization().Routing.MatchMode.Should().Be(EventSinkRouteMatchMode.FirstMatch);
    }

    /// <summary>
    ///     Constructing the router pushes a routing snapshot into the store, which is what a
    ///     newly attached client reads to render the routing in force.
    /// </summary>
    [Fact]
    public void Constructor_PublishesRoutingSnapshotToTheStore()
    {
        var store = new LogEventStore();

        _ = new EventSinkRouter(RouteFor("Ems.Pricing"), store);

        var routing = store.CreateInitialization().Routing;
        routing.Routes.Should().ContainSingle();
        routing.Routes[0].LoggerName.Should().Be("Ems.Pricing");
        routing.Routes[0].LoggerNameMatchMode.Should().Be(LoggerNameMatchMode.Prefix);
        routing.Routes[0].Destinations.Should().ContainSingle();
    }

    [Fact]
    public void Constructor_WithWildcardPattern_SnapshotsAsWildcardMatchMode()
    {
        var store = new LogEventStore();

        _ = new EventSinkRouter(RouteFor("*"), store);

        store.CreateInitialization().Routing.Routes[0].LoggerNameMatchMode.Should().Be(LoggerNameMatchMode.Wildcard);
    }

    /// <summary>
    ///     Route configuration is validated once, at construction, so a misconfiguration fails at
    ///     startup rather than silently dropping logs at runtime.
    /// </summary>
    [Fact]
    public void Constructor_WithRouteLackingDestinations_Throws()
    {
        var options = new EventSinkRouteOptions { Routes = [new EventSinkRoute { CategoryPattern = "App" }] };

        var act = () => new EventSinkRouter(options, new LogEventStore());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithPatternEndingInPeriod_Throws()
    {
        var act = () => new EventSinkRouter(RouteFor("App."), new LogEventStore());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithMinimumLevelAboveMaximum_Throws()
    {
        var options = new EventSinkRouteOptions().Route(
            "App",
            route => route.AtLeast(LogLevel.Error).AtMost(LogLevel.Debug).To("Logs", "App")
        );

        var act = () => new EventSinkRouter(options, new LogEventStore());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithNullOptions_Throws()
    {
        var act = () => new EventSinkRouter(null!, new LogEventStore());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Route_WithNullEvent_Throws()
    {
        var router = new EventSinkRouter(RouteFor("App"), new LogEventStore());

        var act = () => router.Route(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RouteOptions_WithBlankPattern_Throws()
    {
        var act = () => new EventSinkRouteOptions().Route(" ", route => route.To("Logs", "App"));

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Destinations are configurable as "Category/Name" strings so they can come from a config
    ///     file. A malformed value must fail loudly rather than produce a half-built destination.
    /// </summary>
    [Fact]
    public void DestinationConverter_ParsesCategorySlashName()
    {
        var converter = TypeDescriptor.GetConverter(typeof(EventSinkDestination));

        var destination = (EventSinkDestination)converter.ConvertFrom("Logs/App")!;

        destination.SinkCategory!.Value.Should().Be("Logs");
        destination.SinkName!.Value.Should().Be("App");
    }

    [Theory]
    [InlineData("NoSeparator")]
    [InlineData("Too/Many/Parts")]
    [InlineData("/MissingCategory")]
    [InlineData("MissingName/")]
    public void DestinationConverter_WithMalformedValue_Throws(string value)
    {
        var converter = TypeDescriptor.GetConverter(typeof(EventSinkDestination));

        var act = () => converter.ConvertFrom(value);

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void RoutersSharingAStore_KeepBothRoutesInRegistrationOrder()
    {
        var store = new LogEventStore();
        using var first = new EventSinkRouter(RouteFor("First"), store);
        using var second = new EventSinkRouter(RouteFor("Second"), store);

        first.Route(Event("First")).Should().Be(1);
        second.Route(Event("Second")).Should().Be(1);

        LogStreamInitialization initialization = store.CreateInitialization();
        initialization.ReplayEvents.Should().HaveCount(2);
        initialization.Routing.Routes.Select(route => route.LoggerName).Should().Equal("First", "Second");
        initialization.Routing.Routes.Select(route => route.Order).Should().Equal(0, 1);
    }

    [Fact]
    public void Dispose_RemovesOnlyThatRoutersRoutesAndSupersedesSubscribers()
    {
        var store = new LogEventStore();
        var first = new EventSinkRouter(RouteFor("First"), store);
        using var second = new EventSinkRouter(RouteFor("Second"), store);
        using LogEventStore.LogEventStoreSubscription subscription = store.CreateSubscription();

        first.Dispose();
        first.Dispose();

        subscription.EndReason.Should().Be(SubscriptionEndReason.Superseded);
        store.CreateInitialization().Routing.Routes.Should().ContainSingle().Which.LoggerName.Should().Be("Second");
    }

    [Fact]
    public void RouterRegistration_RetainsConfiguredBaseRoutes()
    {
        var store = new LogEventStore();
        store.ConfigureRouting(RouteFor("Base").CreateSnapshot());

        using var router = new EventSinkRouter(RouteFor("Adapter"), store);

        LogStreamRoutingConfiguration routing = store.CreateInitialization().Routing;
        routing.Routes.Select(route => route.LoggerName).Should().Equal("Base", "Adapter");
        routing.Routes.Select(route => route.Order).Should().Equal(0, 1);
    }

    [Fact]
    public void ConflictingRouterMode_LeavesExistingRoutingAndSubscribersUntouched()
    {
        var store = new LogEventStore();
        using var first = new EventSinkRouter(
            new EventSinkRouteOptions()
                .UseMatchMode(EventSinkRouteMatchMode.FirstMatch)
                .Route("First", route => route.To("Logs", "First")),
            store
        );
        using LogEventStore.LogEventStoreSubscription subscription = store.CreateSubscription();

        var act = () =>
            new EventSinkRouter(
                new EventSinkRouteOptions()
                    .UseMatchMode(EventSinkRouteMatchMode.AllMatches)
                    .Route("Second", route => route.To("Logs", "Second")),
                store
            );

        act.Should().Throw<InvalidOperationException>();
        subscription.Events.Completion.IsCompleted.Should().BeFalse();
        store.CreateInitialization().Routing.Routes.Should().ContainSingle().Which.LoggerName.Should().Be("First");
    }

    [Fact]
    public void EmptyRouter_DoesNotSelectTheMatchMode()
    {
        var store = new LogEventStore();
        using var empty = new EventSinkRouter(
            new EventSinkRouteOptions().UseMatchMode(EventSinkRouteMatchMode.AllMatches),
            store
        );

        using var router = new EventSinkRouter(
            new EventSinkRouteOptions()
                .UseMatchMode(EventSinkRouteMatchMode.FirstMatch)
                .Route("App", route => route.To("Logs", "App")),
            store
        );

        store.CreateInitialization().Routing.MatchMode.Should().Be(EventSinkRouteMatchMode.FirstMatch);
    }

    [Fact]
    public void Reconfigure_ReplacesItsContributionWhenTheMatchModeChanges()
    {
        var store = new LogEventStore();
        using var router = new EventSinkRouter(RouteFor("Before"), store);

        router.Reconfigure(
            new EventSinkRouteOptions()
                .UseMatchMode(EventSinkRouteMatchMode.FirstMatch)
                .Route("After", route => route.To("Logs", "After"))
        );

        LogStreamRoutingConfiguration routing = store.CreateInitialization().Routing;
        routing.MatchMode.Should().Be(EventSinkRouteMatchMode.FirstMatch);
        routing.Routes.Should().ContainSingle().Which.LoggerName.Should().Be("After");
    }

    [Fact]
    public void Dispose_WhileReconfigureWaitsForTheStore_RemovesTheReplacementContribution()
    {
        var store = new LogEventStore();
        var router = new EventSinkRouter(RouteFor("Before"), store);
        object sync = typeof(LogEventStore)
            .GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(store)!;
        var reconfigure = new Thread(() => router.Reconfigure(RouteFor("After")));
        var dispose = new Thread(router.Dispose);

        lock (sync)
        {
            reconfigure.Start();
            SpinWait
                .SpinUntil(() => (reconfigure.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(1))
                .Should()
                .BeTrue();
            dispose.Start();
            SpinWait
                .SpinUntil(() => (dispose.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(1))
                .Should()
                .BeTrue();
        }

        reconfigure.Join(TimeSpan.FromSeconds(1)).Should().BeTrue();
        dispose.Join(TimeSpan.FromSeconds(1)).Should().BeTrue();
        store.CreateInitialization().Routing.Routes.Should().BeEmpty();
    }
}
