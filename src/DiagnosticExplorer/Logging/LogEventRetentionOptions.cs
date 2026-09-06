namespace DiagnosticExplorer.Logging;

/// <summary>
///     How much of a log stream <see cref="LogEventStore" /> keeps for replay. Both limits apply:
///     an event is dropped once it is older than <see cref="MaxAgeMinutes" /> or once the stream
///     exceeds <see cref="MaxEvents" />, whichever bites first.
/// </summary>
public sealed class LogEventRetentionOptions
{
    public const int DefaultMaxEvents = 5000;
    public const double DefaultMaxAgeMinutes = 5;

    public int MaxEvents { get; set; } = DefaultMaxEvents;

    public double MaxAgeMinutes { get; set; } = DefaultMaxAgeMinutes;

    public LogEventRetentionOptions WithMaxEvents(int maxEvents)
    {
        MaxEvents = maxEvents;
        return this;
    }

    public LogEventRetentionOptions WithMaxAge(TimeSpan maxAge)
    {
        MaxAgeMinutes = maxAge.TotalMinutes;
        return this;
    }

    /// <summary>
    ///     Validates and copies, so a caller mutating the options it passed in cannot retune a live
    ///     store's retention behind its lock.
    /// </summary>
    internal LogEventRetentionOptions CloneAndValidate()
    {
        if (MaxEvents <= 0)
        {
            throw new InvalidOperationException("The log stream maximum event count must be greater than zero.");
        }

        // The upper bound is not pedantry: Prune does TimeSpan.FromMinutes(MaxAgeMinutes) on every
        // publish, which throws for NaN, infinity, or anything beyond TimeSpan's range. Rejecting
        // here turns a plausible "never expire" misconfiguration into a startup failure instead of
        // an exception on the caller's logging thread. Note NaN survives the `<= 0` test above,
        // since every comparison with NaN is false.
        if (MaxAgeMinutes <= 0 || double.IsNaN(MaxAgeMinutes) || MaxAgeMinutes > TimeSpan.MaxValue.TotalMinutes)
        {
            throw new InvalidOperationException(
                "The log stream maximum age must be a positive, finite number of minutes within the range of TimeSpan."
            );
        }

        return new LogEventRetentionOptions { MaxEvents = MaxEvents, MaxAgeMinutes = MaxAgeMinutes };
    }
}
