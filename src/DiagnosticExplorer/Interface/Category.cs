using System;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf;

namespace DiagnosticExplorer.Interface;

[ProtoContract(UseProtoMembersOnly = true)]
public class Category
{
    public Category()
    {
        Properties = [];
    }

    public Category(string name)
        : this()
    {
        Name = name;
    }

    [ProtoMember(1)]
    public string Name { get; set; }

    [ProtoMember(2)]
    public string OperationSet { get; set; }

    [ProtoMember(3)]
    public List<Property> Properties { get; set; }

    internal object ValueObject { get; set; }
}

public static class CategoryExtensions
{
    private static readonly StringComparer _ignoreCase = StringComparer.CurrentCultureIgnoreCase;

    public static Category FindByName(this IEnumerable<Category> list, string name)
    {
        Guard.NotNull(list, nameof(list));

        return list.FirstOrDefault(x => _ignoreCase.Equals(x.Name, name));
    }
}
