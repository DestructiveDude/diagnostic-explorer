using System;
using System.Globalization;

namespace DiagnosticExplorer.Props;

public enum KpiTargetProperty
{
    Date,
    DateElapsed,
    DateTimeUntil,
    Rate,
    RateTotal,
}

[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public class KpiAttribute : Attribute
{
    public KpiAttribute() { }

    public KpiAttribute(string minSample, string maxSample)
    {
        SampleMinInterval = TimeSpan.Parse(minSample, CultureInfo.InvariantCulture);
        SampleMaxInterval = TimeSpan.Parse(maxSample, CultureInfo.InvariantCulture);
    }

    public KpiAttribute(KpiTargetProperty target)
    {
        Target = target;
    }

    public KpiAttribute(KpiTargetProperty target, string minSample, string maxSample)
    {
        Target = target;
        SampleMinInterval = TimeSpan.Parse(minSample, CultureInfo.InvariantCulture);
        SampleMaxInterval = TimeSpan.Parse(maxSample, CultureInfo.InvariantCulture);
    }

    public KpiTargetProperty? Target { get; set; }

    public TimeSpan? SampleMinInterval { get; set; }

    public TimeSpan? SampleMaxInterval { get; set; }

    public bool Exclude { get; set; }
}
