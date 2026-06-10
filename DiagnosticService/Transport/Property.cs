using System.Collections.Generic;

namespace Diagnostics.Service.Common.Transport;

public class Property
{
    public string Name { get; set; } = null!;

    public string Value { get; set; } = null!;

    public bool CanSet { get; set; }

    public string? Path { get; set; }

    public static List<Property> Map(string path, List<DiagnosticExplorer.Property> properties)
    {
        List<Property> result = [];
        foreach (DiagnosticExplorer.Property property in properties)
        {
            Property groupResult = new()
            {
                Name = property.Name,
                CanSet = property.CanSet,
                Path = property.CanSet ? (path + '|' + property.Name) : null,
                Value = property.Value
            };
            result.Add(groupResult);
        }

        return result;
    }

}