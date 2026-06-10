using System;

namespace Diagnostics.Service.Common.Transport;

public class Event
{

    public string Id { get; set; } = null!;

    public DateTime Date { get; set; }

    public string Message { get; set; } = null!;

    public string? Detail { get; set; }

    public string Severity { get; set; } = null!;
}