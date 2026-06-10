using DiagnosticExplorer.Events;
using DiagnosticExplorer.Interface;

namespace Diagnostic.Service.Transport;

public class SubCategory
{
    public SubCategory()
    {
    }

    public SubCategory(PropertyBag subcategory)
    {
        Name = subcategory.Name;
        Path = subcategory.Category + '|' + subcategory.Name;
        PropertyGroups = PropertyGroup.Map(Path, subcategory.Categories).ToArray();
    }

    public string Name { get; set; } = null!;

    public PropertyGroup[] PropertyGroups { get; set; } = Array.Empty<PropertyGroup>();

    public SystemEvent[] Events { get; set; } = Array.Empty<SystemEvent>();

    public string Path { get; set; } = null!;

    public Operation[] Operations { get; set; } = Array.Empty<Operation>();

}