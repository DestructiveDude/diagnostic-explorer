using System.Collections.Generic;

namespace Diagnostics.Service.Common.Transport;

public class Category
{
    public Category()
    {
    }

    public Category(string name)
    {
        Name = name;
    }

    public string Name { get; set; } = null!;

    public List<SubCategory> SubCategories { get; set; } = [];


}