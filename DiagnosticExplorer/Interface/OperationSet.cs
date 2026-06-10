using System.Collections.Generic;
using System.Runtime.Serialization;
using ProtoBuf;

namespace DiagnosticExplorer;

[ProtoContract(UseProtoMembersOnly = true)]
public class OperationSet
{
    public OperationSet()
    {
        Operations = [];
    }

    [ProtoMember(1)]
    public string Id { get; set; }

    [ProtoMember(2)]
    public List<Operation> Operations { get; set; }

}