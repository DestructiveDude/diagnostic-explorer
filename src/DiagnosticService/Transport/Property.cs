namespace Diagnostic.Service.Transport;

public class Property
{
    public string Name { get; set; } = null!;

    public string Value { get; set; } = null!;

    public bool CanSet { get; set; }

    public string? Path { get; set; }

    public static List<Property> Map(string path, List<DiagnosticExplorer.Interface.Property> properties)
    {
        List<Property> result = [];
        foreach (var property in properties)
        {
            Property groupResult = new()
            {
                Name = property.Name,
                CanSet = property.CanSet,
                Path = property.CanSet ? path + '|' + property.Name : null,
                Value = property.Value,
            };
            result.Add(groupResult);
        }

        return result;
    }
}
