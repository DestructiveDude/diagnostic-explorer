using System;
using log4net;
using log4net.Core;

namespace WidgetSample;

public static class LoggingExtensions
{
    public static void Notice(this ILog log, string message, params object[] args)
    {
        log.Logger.Log(new LoggingEvent(BuildData(log, message, null, args)));
    }

    // Overload that attaches the exception.
    public static void Notice(this ILog log, string message, Exception exception, params object[] args)
    {
        log.Logger.Log(new LoggingEvent(BuildData(log, message, exception, args)));
    }

    private static LoggingEventData BuildData(ILog log, string message, Exception exception, object[] args)
    {
        LoggingEventData data = new()
        {
            Message = message,
            Level = Level.Notice,
            LoggerName = log.Logger.Name,
            TimeStampUtc = DateTime.UtcNow
        };

        if (args?.Length > 0)
        {
            try
            {
                data.Message = string.Format(message, args);
            }
            catch (Exception ex)
            {
                data.Message += $" (logging format exception): {ex.Message}";
            }
        }

        if (exception != null)
        {
            data.ExceptionString = exception.ToString();
        }

        return data;
    }
}
