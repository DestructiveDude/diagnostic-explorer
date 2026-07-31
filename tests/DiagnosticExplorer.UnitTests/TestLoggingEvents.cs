using log4net.Core;

namespace DiagnosticExplorer.UnitTests;

/// <summary>
///     Builds minimal real <see cref="LoggingEvent" /> instances for the Log4Net
///     failure-isolation tests.
/// </summary>
internal static class TestLoggingEvents
{
    public static LoggingEvent NewEvent(string message) =>
        new(
            new LoggingEventData
            {
                Domain = "test",
                LoggerName = "test",
                Level = Level.Info,
                Message = message,
                TimeStampUtc = DateTime.UtcNow,
            }
        );
}
