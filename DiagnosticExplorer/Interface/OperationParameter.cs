using ProtoBuf;

namespace DiagnosticExplorer.Interface;

[ProtoContract(UseProtoMembersOnly = true)]
public class OperationParameter
{
    public OperationParameter() { }

    public OperationParameter(string name, string type)
    {
        Name = name;
        Type = type;
    }

    [ProtoMember(1)]
    public string Name { get; set; }

    [ProtoMember(2)]
    public string Type { get; set; }
}
