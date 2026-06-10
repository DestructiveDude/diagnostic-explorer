using System;
using ProtoBuf;

namespace Diagnostics.Service.Common.Transport;

[ProtoContract]
public class RetroQuery
{
    public int SearchId { get; set; }
    public int MaxRecords { get; set; }
    public int MinLevel { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? Machine { get; set; }
    public string? Process { get; set; }
    public string? User { get; set; }
    public string? Message { get; set; }

    public RetroQuery Clone()
    {
        return (RetroQuery) MemberwiseClone();
    }
}