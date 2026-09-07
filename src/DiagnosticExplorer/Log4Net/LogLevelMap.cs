using log4net.Core;
using Microsoft.Extensions.Logging;

namespace DiagnosticExplorer.Log4Net;

/// <summary>
///     Maps log4net level values (spaced in multiples of 10,000) down to
///     Microsoft.Extensions.Logging.LogLevel ordinals (Trace=0 to None=6).
/// </summary>
/// <remarks>
///     <para>
///         This is the scheme the LOG STREAM carries — <c>LogStreamEvent.Level</c>, and the
///         MinLevel/MaxLevel a route is matched on. log4net is on its way out, and this is the
///         single boundary where its richer level set is folded into the Microsoft scheme.
///     </para>
///     <para>
///         It is NOT the scheme <see cref="DiagnosticMsg" /> carries. The retro feed is a separate
///         path: <c>DiagnosticRetroAppender</c> writes <c>loggingEvent.Level.Value</c> straight
///         through, so retro messages hold raw log4net values (10 000..120 000) and the web client
///         reads them with its own log4net-scaled <c>Level</c>. Do not "make it consistent" by
///         folding retro through here — that would silently drop every retro event to the bottom of
///         the display scale, which is exactly the bug the realtime feed had before its display
///         mapping was added.
///     </para>
/// </remarks>
public static class LogLevelMap
{
    /// <summary>Map a log4net <see cref="Level" /> to a Microsoft LogLevel ordinal.</summary>
    public static int ToMicrosoftOrdinal(this Level level)
    {
        return level == null ? (int)LogLevel.Information : ToMicrosoftOrdinal(level.Value);
    }

    /// <summary>Map a raw log4net level value to a Microsoft LogLevel ordinal.</summary>
    public static int ToMicrosoftOrdinal(int log4netValue)
    {
        // log4net: Verbose=10000, Trace=20000, Debug=30000, Info=40000, Notice=50000,
        // Warn=60000, Error=70000, Severe=80000, Critical=90000, Alert=100000,
        // Fatal=110000, Emergency=120000, Off=int.MaxValue
        if (log4netValue >= Level.Off.Value)
        {
            return (int)LogLevel.None; // 6
        }
        if (log4netValue >= Level.Severe.Value)
        {
            return (int)LogLevel.Critical; // 5  Severe/Critical/Alert/Fatal/Emergency
        }
        if (log4netValue >= Level.Error.Value)
        {
            return (int)LogLevel.Error; // 4
        }
        if (log4netValue >= Level.Warn.Value)
        {
            return (int)LogLevel.Warning; // 3
        }
        if (log4netValue >= Level.Info.Value)
        {
            return (int)LogLevel.Information; // 2  Info/Notice
        }
        if (log4netValue >= Level.Debug.Value)
        {
            return (int)LogLevel.Debug; // 1
        }
        return (int)LogLevel.Trace; // 0  Trace/Verbose/All
    }
}
