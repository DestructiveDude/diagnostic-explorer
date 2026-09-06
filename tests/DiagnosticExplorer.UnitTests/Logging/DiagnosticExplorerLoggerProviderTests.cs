using AwesomeAssertions;
using DiagnosticExplorer.Extensions.Logging;
using DiagnosticExplorer.Logging;
using Microsoft.Extensions.Logging;

namespace DiagnosticExplorer.UnitTests.Logging;

/// <summary>
///     The Microsoft.Extensions.Logging provider. Its distinguishing job over the other adapters is
///     folding structured state, scopes and the event id into the detail pane, since those have no
///     equivalent in the log4net path.
/// </summary>
public class DiagnosticExplorerLoggerProviderTests
{
    private static EventSinkRouteOptions RoutesFor(string pattern) =>
        new EventSinkRouteOptions().Route(pattern, route => route.To("Logs", "App"));

    [Fact]
    public void Log_PublishesAMatchingEventWithItsCategoryAndLevel()
    {
        var store = new LogEventStore();
        using var provider = new DiagnosticExplorerLoggerProvider(RoutesFor("Widgets"), store);

        provider.CreateLogger("Widgets.Component").LogWarning("Paint failed");

        LogStreamEvent published = Replay(store).Should().ContainSingle().Subject;
        published.LoggerCategory.Should().Be("Widgets.Component");
        published.Level.Should().Be((int)LogLevel.Warning);
        published.Message.Should().Be("Paint failed");
    }

    [Fact]
    public void Log_WhenNoRouteMatches_PublishesNothing()
    {
        var store = new LogEventStore();
        using var provider = new DiagnosticExplorerLoggerProvider(RoutesFor("Widgets"), store);

        provider.CreateLogger("Gadgets").LogWarning("Paint failed");

        Replay(store).Should().BeEmpty();
    }

    /// <summary>
    ///     LogLevel.None is the "log nothing" sentinel, not a severity. Publishing it would put an
    ///     event on the stream that no caller ever intended to emit.
    /// </summary>
    [Fact]
    public void IsEnabled_IsFalseForNoneEvenWhenARouteMatchesEverything()
    {
        using var provider = new DiagnosticExplorerLoggerProvider(RoutesFor("*"), new LogEventStore());

        provider.CreateLogger("Widgets").IsEnabled(LogLevel.None).Should().BeFalse();
    }

    /// <summary>
    ///     Structured message properties are what make a templated log line worth keeping, so they
    ///     have to survive into the detail pane — minus {OriginalFormat}, which is the template
    ///     itself and already visible as the rendered message.
    /// </summary>
    [Fact]
    public void Log_FoldsStructuredStateIntoDetailButDropsTheMessageTemplate()
    {
        var store = new LogEventStore();
        using var provider = new DiagnosticExplorerLoggerProvider(RoutesFor("Widgets"), store);

        provider.CreateLogger("Widgets").LogInformation("Painted {WidgetId} in {Colour}", 42, "red");

        string detail = Replay(store).Should().ContainSingle().Subject.Detail!;
        detail.Should().Contain("State.WidgetId: 42").And.Contain("State.Colour: red");
        detail.Should().NotContain("OriginalFormat");
    }

    [Fact]
    public void Log_FoldsScopesIntoDetail()
    {
        var store = new LogEventStore();
        using var provider = new DiagnosticExplorerLoggerProvider(RoutesFor("Widgets"), store);
        provider.SetScopeProvider(new LoggerExternalScopeProvider());
        ILogger logger = provider.CreateLogger("Widgets");

        using (logger.BeginScope(new Dictionary<string, object?> { ["OrderId"] = 7 }))
        {
            logger.LogInformation("Painted");
        }

        Replay(store).Should().ContainSingle().Subject.Detail.Should().Contain("Scope.OrderId: 7");
    }

    /// <summary>
    ///     BeginScope with a bare value is ordinary usage, and its whole purpose is correlation, so
    ///     dropping it for not being a property list loses the only thing it carried.
    /// </summary>
    [Fact]
    public void Log_FoldsAScalarScopeIntoDetail()
    {
        var store = new LogEventStore();
        using var provider = new DiagnosticExplorerLoggerProvider(RoutesFor("Widgets"), store);
        provider.SetScopeProvider(new LoggerExternalScopeProvider());
        ILogger logger = provider.CreateLogger("Widgets");

        using (logger.BeginScope("RequestId=7"))
        {
            logger.LogInformation("Painted");
        }

        Replay(store).Should().ContainSingle().Subject.Detail.Should().Contain("Scope: RequestId=7");
    }

    /// <summary>
    ///     The same leniency must NOT extend to the message state. A scalar there is the message,
    ///     which is already the headline, so recording it again would double every non-templated
    ///     line in the detail pane.
    /// </summary>
    [Fact]
    public void Log_DoesNotRepeatAScalarMessageStateInDetail()
    {
        var store = new LogEventStore();
        using var provider = new DiagnosticExplorerLoggerProvider(RoutesFor("Widgets"), store);

        provider.CreateLogger("Widgets").Log(LogLevel.Information, default, "Painted", null, (state, _) => state);

        LogStreamEvent published = Replay(store).Should().ContainSingle().Subject;
        published.Message.Should().Be("Painted");
        published.Detail.Should().BeNull();
    }

    [Fact]
    public void Log_FoldsTheExceptionAndEventIdIntoDetail()
    {
        var store = new LogEventStore();
        using var provider = new DiagnosticExplorerLoggerProvider(RoutesFor("Widgets"), store);

        provider
            .CreateLogger("Widgets")
            .LogError(new EventId(7, "PaintFailed"), new InvalidOperationException("boom"), "Painted");

        LogStreamEvent published = Replay(store).Should().ContainSingle().Subject;
        published.EventId.Should().Be(7);
        published.EventName.Should().Be("PaintFailed");
        published.Detail.Should().Contain("boom");
    }

    /// <summary>
    ///     A multi-line message is cut at the first newline for the event list, with the whole
    ///     message kept in the detail pane.
    /// </summary>
    [Fact]
    public void Log_SplitsAMultiLineMessageIntoHeadlineAndDetail()
    {
        var store = new LogEventStore();
        using var provider = new DiagnosticExplorerLoggerProvider(RoutesFor("Widgets"), store);

        provider.CreateLogger("Widgets").LogInformation("first line\nsecond line");

        LogStreamEvent published = Replay(store).Should().ContainSingle().Subject;
        published.Message.Should().Be("first line");
        published.Detail.Should().Contain("second line");
    }

    /// <summary>The provider is a cache: one logger instance per category, however often asked.</summary>
    [Fact]
    public void CreateLogger_ReturnsTheSameInstanceForTheSameCategory()
    {
        using var provider = new DiagnosticExplorerLoggerProvider(RoutesFor("*"), new LogEventStore());

        provider.CreateLogger("Widgets").Should().BeSameAs(provider.CreateLogger("Widgets"));
    }

    private static LogStreamEvent[] Replay(LogEventStore store)
    {
        using LogEventStore.LogEventStoreSubscription subscription = store.CreateSubscription();
        return subscription.Initialization!.ReplayEvents;
    }
}
