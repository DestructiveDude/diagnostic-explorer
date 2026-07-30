using System;

namespace DiagnosticExplorer.Log4Net.Util;

/// <summary>
///     Provides an abstraction for the system clock
/// </summary>
public static class SystemDateTime
{
    /// <summary>
    ///     Returns the current date and time. The clock is replaceable only from within the
    ///     library or the unit-test assembly (<c>internal set</c>, exposed via InternalsVisibleTo)
    ///     so external code can read it but cannot stomp the global clock.
    /// </summary>
    /// <remarks>
    ///     This stays a static seam rather than an injected <c>IClock</c>/<c>TimeProvider</c> by
    ///     design: the only consumer (<see cref="AppenderProxy" />) is instantiated by log4net from
    ///     XML configuration, not through DI, and this is a published <c>netstandard2.0</c> package,
    ///     so constructor injection isn't available at the appender boundary. Restricting the setter
    ///     to <c>internal</c> removes the public-mutable-global concern while keeping the test seam.
    /// </remarks>
    public static Func<DateTime> Now { get; internal set; } = () => DateTime.Now;

    public static Func<DateTime> UtcNow { get; internal set; } = () => DateTime.UtcNow;
}
