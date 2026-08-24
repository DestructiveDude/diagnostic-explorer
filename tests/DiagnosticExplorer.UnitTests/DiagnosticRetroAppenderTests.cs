using AwesomeAssertions;
using DiagnosticExplorer.Log4Net;
using Microsoft.Extensions.Time.Testing;

namespace DiagnosticExplorer.UnitTests;

public sealed class DiagnosticRetroAppenderTests
{
    [Fact]
    public void Append_UsesInjectedTimeProvider()
    {
        DateTimeOffset now = new(2026, 8, 12, 9, 30, 0, TimeSpan.Zero);
        FakeTimeProvider timeProvider = new(now);
        DiagnosticMsg? captured = null;
        DiagnosticRetroAppender.SetLoggingAction(message => captured = message);

        try
        {
            var appender = new TestAppender(timeProvider) { Layout = new log4net.Layout.PatternLayout("%message") };

            appender.AppendEvent(TestLoggingEvents.NewEvent("message"));

            captured.Should().NotBeNull();
            captured.Date.Should().Be(now.UtcDateTime);
        }
        finally
        {
            DiagnosticRetroAppender.SetLoggingAction(null);
        }
    }

    private sealed class TestAppender(TimeProvider timeProvider) : DiagnosticRetroAppender(timeProvider)
    {
        public void AppendEvent(log4net.Core.LoggingEvent loggingEvent) => Append(loggingEvent);
    }
}
