using System;
using ProtoBuf;

namespace DiagnosticExplorer;


[ProtoContract(UseProtoMembersOnly = true)]
public class DiagnosticMsg
{

    [ProtoMember(1)]
    public int Level { get; set; }

    [ProtoMember(2)]
    public DateTime Date { get; set; }

    [ProtoMember(3)]
    public string Machine { get; set; }

    [ProtoMember(4)]
    public string Process { get; set; }

    [ProtoMember(5)]
    public string User { get; set; }

    [ProtoMember(6)]
    public string Category { get; set; }

    [ProtoMember(7)]
    public string Message { get; set; }


    [ProtoMember(8)]
    public string Environment { get; set; }

    [ProtoMember(9)]
    public string MsgId { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 24);

}