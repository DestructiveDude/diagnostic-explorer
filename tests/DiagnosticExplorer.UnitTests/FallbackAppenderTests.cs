using AwesomeAssertions;
using DiagnosticExplorer.Log4Net;
using log4net.Appender;
using log4net.Core;

namespace DiagnosticExplorer.UnitTests;

/// <summary>
///     <see cref="FallbackAppender" /> chains through its wrapped appenders: a failing
///     primary must hand the event to the next appender, a healthy primary must starve
///     the fallback. This is the last line of defence before a host app's logs are lost.
///     (DE-11)
/// </summary>
public sealed class FallbackAppenderTests : IDisposable
{
    private readonly FallbackAppender _appender = new() { Name = "fallback-under-test" };

    public void Dispose()
    {
        // ActivateOptions registers the appender with the global DiagnosticManager;
        // Close unregisters it again.
        _appender.Close();
    }

    [Fact]
    public void DoAppend_WhenPrimaryFails_FallsThroughToFallback()
    {
        var primary = new FakeAppenderSkeleton("primary", fails: true);
        var fallback = new FakeAppenderSkeleton("fallback");
        _appender.AddAppender(primary);
        _appender.AddAppender(fallback);
        _appender.ActivateOptions();

        _appender.DoAppend(TestLoggingEvents.NewEvent("hello"));

        primary.AppendCount.Should().Be(1);
        fallback.AppendCount.Should().Be(1);
    }

    [Fact]
    public void DoAppend_WhenPrimaryIsHealthy_DoesNotTouchFallback()
    {
        var primary = new FakeAppenderSkeleton("primary");
        var fallback = new FakeAppenderSkeleton("fallback");
        _appender.AddAppender(primary);
        _appender.AddAppender(fallback);
        _appender.ActivateOptions();

        _appender.DoAppend(TestLoggingEvents.NewEvent("hello"));

        primary.AppendCount.Should().Be(1);
        fallback.AppendCount.Should().Be(0);
    }

    /// <summary>
    ///     AppenderProxy only wraps AppenderSkeleton targets, so the fake must be a real
    ///     AppenderSkeleton whose Append throws when told to fail.
    /// </summary>
    private sealed class FakeAppenderSkeleton : AppenderSkeleton
    {
        private readonly bool _fails;

        public FakeAppenderSkeleton(string name, bool fails = false)
        {
            Name = name;
            _fails = fails;
        }

        public int AppendCount { get; private set; }

        protected override void Append(LoggingEvent loggingEvent)
        {
            AppendCount++;
            if (_fails)
            {
                throw new InvalidOperationException("primary target is down");
            }
        }
    }
}
