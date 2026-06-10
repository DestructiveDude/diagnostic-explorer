using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net;
using log4net.Core;

namespace WidgetSample;

public static class LoggingExtensions
{
    public static void Notice(this ILog log, string message, params object[] args)
    {
        log.Logger.Log(new LoggingEvent(BuildData(message, null, args)));
    }

    // Overload that actually attaches the exception. The string-only overload above treats a
    // trailing Exception as a string.Format arg and silently drops it (the demo's bNotice_Click
    // hit exactly that anti-pattern); callers with an exception should use this overload.
    public static void Notice(this ILog log, string message, Exception exception, params object[] args)
    {
        log.Logger.Log(new LoggingEvent(BuildData(message, exception, args)));
    }

    private static LoggingEventData BuildData(string message, Exception exception, object[] args)
    {
        LoggingEventData data = new() { Message = message, Level = Level.Notice };

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
