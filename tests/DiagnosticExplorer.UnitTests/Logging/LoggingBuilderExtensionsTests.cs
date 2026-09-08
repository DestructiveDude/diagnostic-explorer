using AwesomeAssertions;
using DiagnosticExplorer.Extensions.Logging;
using DiagnosticExplorer.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DiagnosticExplorer.UnitTests.Logging;

/// <summary>
///     The three ways a host registers the Microsoft.Extensions.Logging provider. Each is asserted
///     end to end — build the provider, log through it, read the stream — because a registration
///     that silently attaches no routes looks identical to one that works until an event arrives.
/// </summary>
[Collection(DiagnosticConfigurationCollection.Name)]
public sealed class LoggingBuilderExtensionsTests : IDisposable
{
    public void Dispose() => DiagnosticManager.UseConfiguration(new DiagnosticConfiguration());

    [Fact]
    public void AddDiagnosticExplorer_WithOptions_RoutesThroughToTheStore()
    {
        var store = new LogEventStore();
        EventSinkRouteOptions options = new EventSinkRouteOptions().Route("Widgets", route => route.To("Logs", "App"));

        using ServiceProvider services = Build(builder => builder.AddDiagnosticExplorer(options, store));

        services.GetRequiredService<ILoggerFactory>().CreateLogger("Widgets").LogInformation("Painted");
        Replay(store).Should().ContainSingle();
    }

    [Fact]
    public void AddDiagnosticExplorer_HostDisposalRemovesTheProvidersRoutingContribution()
    {
        var store = new LogEventStore();
        EventSinkRouteOptions options = new EventSinkRouteOptions().Route("Widgets", route => route.To("Logs", "App"));
        ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddDiagnosticExplorer(options, store));

        factory.CreateLogger("Widgets").LogInformation("Painted");
        store.CreateInitialization().Routing.Routes.Should().ContainSingle();
        factory.Dispose();

        store.CreateInitialization().Routing.Routes.Should().BeEmpty();
    }

    [Fact]
    public void AddDiagnosticExplorer_WithAConfigureAction_RoutesThroughToTheStore()
    {
        var store = new LogEventStore();

        using ServiceProvider services = Build(builder =>
            builder.AddDiagnosticExplorer(options => options.Route("Widgets", route => route.To("Logs", "App")), store)
        );

        services.GetRequiredService<ILoggerFactory>().CreateLogger("Widgets").LogInformation("Painted");
        Replay(store).Should().ContainSingle();
    }

    /// <summary>
    ///     Binding from IConfiguration is the path a deployed host actually uses, and the one that
    ///     exercises the TypeConverters on the route DTOs — a "Category/Name" destination string has
    ///     to survive the bind.
    /// </summary>
    [Fact]
    public void AddDiagnosticExplorer_WithConfiguration_BindsRoutesAndRoutesThroughToTheStore()
    {
        var store = new LogEventStore();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Routes:0:CategoryPattern"] = "Widgets",
                    ["Routes:0:Destinations:0"] = "Logs/App",
                }
            )
            .Build();

        using ServiceProvider services = Build(builder => builder.AddDiagnosticExplorer(configuration, store));

        services.GetRequiredService<ILoggerFactory>().CreateLogger("Widgets").LogInformation("Painted");
        Replay(store).Should().ContainSingle();
    }

    [Fact]
    public void AddDiagnosticExplorer_WithANullConfigureAction_Throws()
    {
        Action add = () => Build(builder => builder.AddDiagnosticExplorer((Action<EventSinkRouteOptions>)null!));

        add.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddDiagnosticExplorer_WithANullConfiguration_Throws()
    {
        Action add = () => Build(builder => builder.AddDiagnosticExplorer((IConfiguration)null!));

        add.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddDiagnosticExplorer_WithoutOptions_UsesTheCurrentConfiguration()
    {
        DiagnosticManager.Configure(configure =>
            configure.ConfigureEventRouting(routes => routes.Route("Widgets", route => route.To("Logs", "App")))
        );
        int before = DiagnosticManager.LogEventStore.CreateInitialization().ReplayEvents.Length;

        using ServiceProvider services = Build(builder => builder.AddDiagnosticExplorer());
        services.GetRequiredService<ILoggerFactory>().CreateLogger("Widgets").LogInformation("Painted");

        DiagnosticManager.LogEventStore.CreateInitialization().ReplayEvents.Should().HaveCount(before + 1);
    }

    private static ServiceProvider Build(Action<ILoggingBuilder> configure) =>
        new ServiceCollection().AddLogging(configure).BuildServiceProvider();

    private static LogStreamEvent[] Replay(LogEventStore store)
    {
        using LogEventStore.LogEventStoreSubscription subscription = store.CreateSubscription();
        return subscription.Initialization!.ReplayEvents;
    }
}
