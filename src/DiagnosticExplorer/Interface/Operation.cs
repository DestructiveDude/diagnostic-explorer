using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DiagnosticExplorer.Util;
using ProtoBuf;

namespace DiagnosticExplorer.Interface;

[ProtoContract(UseProtoMembersOnly = true)]
public class Operation
{
    public Operation() { }

    public Operation(MethodInfo methodInfo)
        : this()
    {
        MethodInfo = methodInfo;
        Signature = methodInfo.Name;

        Parameters = methodInfo
            .GetParameters()
            .Select(x => new OperationParameter(x.Name, TypeUtil.GetFriendlyTypeName(x.ParameterType)))
            .ToList();

        var paramTypes = Parameters.Select(x => x.Type).ToArray();
        Signature = string.Format("{0}({1})", methodInfo.Name, string.Join(", ", paramTypes));
        ReturnType = TypeUtil.GetFriendlyTypeName(methodInfo.ReturnType);
    }

    [ProtoMember(1)]
    public string ReturnType { get; set; }

    [ProtoMember(2)]
    public string Signature { get; set; }

    [ProtoMember(3)]
    public string Description { get; set; }

    [ProtoMember(4)]
    public List<OperationParameter> Parameters { get; set; }

    internal MethodInfo MethodInfo { get; private set; }
}
