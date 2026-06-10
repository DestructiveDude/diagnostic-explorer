using log4net.Core;

namespace DiagnosticExplorer.Log4Net.Util;

internal class LoggingEventContext
{
    public LoggingEventContext(LoggingEvent loggingEvent)
    {
        LoggingEvent = loggingEvent;
    }

    public LoggingEvent LoggingEvent { get; set; }

}