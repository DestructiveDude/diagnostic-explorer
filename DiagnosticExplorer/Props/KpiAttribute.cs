using System;

namespace DiagnosticExplorer.Props;


public enum KpiTargetProperty
{
    Date,
    DateElapsed,
    DateTimeUntil,
    Rate,
    RateTotal
}

[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public class KpiAttribute : Attribute
{
    public KpiAttribute()
    {

    }

    public KpiAttribute(string minSample, string maxSample)
    {
        SampleMinInterval = TimeSpan.Parse(minSample);
        SampleMaxInterval = TimeSpan.Parse(maxSample);
    }

    public KpiAttribute(KpiTargetProperty target)
    {
        Target = target;
    }

    public KpiAttribute(KpiTargetProperty target, string minSample, string maxSample)
    {
        Target = target;
        SampleMinInterval = TimeSpan.Parse(minSample);
        SampleMaxInterval = TimeSpan.Parse(maxSample);
    }

    public KpiTargetProperty? Target { get; set; }

    public TimeSpan? SampleMinInterval { get; set; }

    public TimeSpan? SampleMaxInterval { get; set; }

    public bool Exclude { get; set; }
}