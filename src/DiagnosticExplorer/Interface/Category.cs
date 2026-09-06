using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using ProtoBuf;

namespace DiagnosticExplorer;

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

    // Members 4 onward are upstream's presentation model, appended with fresh ProtoMember numbers
    // so an older client simply ignores tags it does not know.

    [ProtoMember(4)]
    public bool CanDrillDown { get; set; }

    [ProtoMember(5)]
    public bool IsExpanded { get; set; }

    [ProtoMember(6)]
    public bool IsExpandedProperty { get; set; }

    [ProtoMember(7)]
    public List<PropertyStatus> Statuses { get; set; }

    [ProtoMember(8)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public StatusIconSize StatusIconSize { get; set; }

    internal object ValueObject { get; set; }

    internal object DrillDownObject { get; set; }

    internal int DrillDownMaxItems { get; set; }
}

public static class CategoryExtensions
{
    private static readonly StringComparer _ignoreCase = StringComparer.CurrentCultureIgnoreCase;

    /// <summary>
    ///     The canonical form of a category name. Blank and "General" both denote the unnamed
    ///     default category, so both normalise to null and therefore compare equal to each other.
    /// </summary>
    public static string NormalizeName(string name)
    {
        return string.IsNullOrWhiteSpace(name) || string.Equals(name, "General", StringComparison.OrdinalIgnoreCase)
            ? null
            : name;
    }

    public static Category FindByName(this IEnumerable<Category> list, string name)
    {
        Guard.NotNull(list, nameof(list));

        name = NormalizeName(name);
        return list.FirstOrDefault(x => _ignoreCase.Equals(x.Name, name));
    }
}
