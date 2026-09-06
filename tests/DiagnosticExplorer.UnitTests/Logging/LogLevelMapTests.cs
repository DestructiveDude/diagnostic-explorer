using AwesomeAssertions;
using DiagnosticExplorer.Log4Net;
using log4net.Core;
using Microsoft.Extensions.Logging;

namespace DiagnosticExplorer.UnitTests.Logging;

/// <summary>
///     log4net has twelve levels and Microsoft.Extensions.Logging has six, so the fold is lossy by
///     construction and the whole point is agreeing exactly where each boundary sits. A level that
///     folds one notch too high silently promotes routine chatter into an error stream.
/// </summary>
public class LogLevelMapTests
{
    [Theory]
    [InlineData("All", LogLevel.Trace)]
    [InlineData("Verbose", LogLevel.Trace)]
    [InlineData("Trace", LogLevel.Trace)]
    [InlineData("Debug", LogLevel.Debug)]
    [InlineData("Info", LogLevel.Information)]
    [InlineData("Notice", LogLevel.Information)]
    [InlineData("Warn", LogLevel.Warning)]
    [InlineData("Error", LogLevel.Error)]
    [InlineData("Severe", LogLevel.Critical)]
    [InlineData("Critical", LogLevel.Critical)]
    [InlineData("Alert", LogLevel.Critical)]
    [InlineData("Fatal", LogLevel.Critical)]
    [InlineData("Emergency", LogLevel.Critical)]
    [InlineData("Off", LogLevel.None)]
    public void ToMicrosoftOrdinal_FoldsEveryLog4NetLevel(string levelName, LogLevel expected)
    {
        Level level = LevelNamed(levelName);

        level.ToMicrosoftOrdinal().Should().Be((int)expected);
    }

    /// <summary>
    ///     A null level reaches here when a log4net event was constructed without one. Information
    ///     is the safe assumption: dropping it would lose the event, and Error would raise a false
    ///     alarm.
    /// </summary>
    [Fact]
    public void ToMicrosoftOrdinal_WhenLevelIsNull_FoldsToInformation()
    {
        // Called as a plain static rather than through extension syntax: invoking an extension
        // method on a null reference reads as a bug at the call site even where it is the
        // behaviour under test.
        LogLevelMap.ToMicrosoftOrdinal(level: null).Should().Be((int)LogLevel.Information);
    }

    /// <summary>
    ///     A custom level sitting between two known ones folds down to the lower neighbour, never
    ///     up. Rounding up would let an installation's bespoke level escalate itself.
    /// </summary>
    [Theory]
    [InlineData(65_000, LogLevel.Warning)]
    [InlineData(75_000, LogLevel.Error)]
    [InlineData(5_000, LogLevel.Trace)]
    public void ToMicrosoftOrdinal_FoldsAnUnknownValueDownToItsLowerNeighbour(int rawValue, LogLevel expected)
    {
        LogLevelMap.ToMicrosoftOrdinal(rawValue).Should().Be((int)expected);
    }

    private static Level LevelNamed(string name) =>
        name switch
        {
            "All" => Level.All,
            "Verbose" => Level.Verbose,
            "Trace" => Level.Trace,
            "Debug" => Level.Debug,
            "Info" => Level.Info,
            "Notice" => Level.Notice,
            "Warn" => Level.Warn,
            "Error" => Level.Error,
            "Severe" => Level.Severe,
            "Critical" => Level.Critical,
            "Alert" => Level.Alert,
            "Fatal" => Level.Fatal,
            "Emergency" => Level.Emergency,
            "Off" => Level.Off,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown log4net level."),
        };
}
