namespace DiagnosticExplorer;

/// <summary>
///     How much of each event sink is kept for replay. Both limits apply: an event is dropped once
///     it is older than <see cref="MaxAgeMinutes" /> or once the sink exceeds
///     <see cref="MaxEventsPerSink" />, whichever bites first.
/// </summary>
/// <remarks>
///     The sibling of <see cref="Logging.LogEventRetentionOptions" />, which governs the log stream
///     rather than the event sinks. They are deliberately separate: a host tunes how much log
///     history it replays independently of how much event history it keeps.
/// </remarks>
public sealed class EventRetentionOptions
{
    public const string ConfigurationSectionKey = "DiagnosticExplorer:EventRetention";
    public const int DefaultMaxEventsPerSink = 1000;
    public const double DefaultMaxAgeMinutes = 30;

    public int MaxEventsPerSink { get; set; } = DefaultMaxEventsPerSink;

    public double MaxAgeMinutes { get; set; } = DefaultMaxAgeMinutes;

    public EventRetentionOptions WithMaxEventsPerSink(int maxEventsPerSink)
    {
        MaxEventsPerSink = maxEventsPerSink;
        return this;
    }

    public EventRetentionOptions WithMaxAge(TimeSpan maxAge)
    {
        MaxAgeMinutes = maxAge.TotalMinutes;
        return this;
    }

    /// <summary>
    ///     Validates and copies, so a caller mutating the options it passed in cannot retune live
    ///     retention afterwards.
    /// </summary>
    internal EventRetentionOptions CloneAndValidate()
    {
        // InvalidOperationException rather than ArgumentOutOfRangeException: these are properties
        // set by a configurator, not arguments, so there is no parameter name to carry. Matches
        // LogEventRetentionOptions, whose guards read the same way.
        if (MaxEventsPerSink < 1)
        {
            throw new InvalidOperationException("The maximum number of events per sink must be at least 1.");
        }

        // Upstream tests only `<= 0` here. That admits NaN, because every comparison with NaN is
        // false, and admits values beyond TimeSpan's range - either of which throws later, at
        // whichever pruning call first builds a TimeSpan from this, rather than at startup. Same
        // defect and same fix as LogEventRetentionOptions.
        if (MaxAgeMinutes <= 0 || double.IsNaN(MaxAgeMinutes) || MaxAgeMinutes > TimeSpan.MaxValue.TotalMinutes)
        {
            throw new InvalidOperationException(
                "The maximum event age must be a positive, finite number of minutes within the range of TimeSpan."
            );
        }

        return new EventRetentionOptions { MaxEventsPerSink = MaxEventsPerSink, MaxAgeMinutes = MaxAgeMinutes };
    }
}
