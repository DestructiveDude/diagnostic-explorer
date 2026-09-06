#region Copyright

// Diagnostic Explorer, a .Net diagnostic toolset
// Copyright (C) 2010 Cameron Elliot
//
// This file is part of Diagnostic Explorer.
//
// Diagnostic Explorer is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Diagnostic Explorer is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License
// along with Diagnostic Explorer.  If not, see <http://www.gnu.org/licenses/>.
//
// http://diagexplorer.sourceforge.net/

#endregion

using System.Collections.Generic;

namespace DiagnosticExplorer;

public class PropertyBag
{
    public PropertyBag()
    {
        Categories = [];
    }

    public PropertyBag(string name)
        : this()
    {
        Name = name;
    }

    public PropertyBag(string name, string category)
        : this(name)
    {
        Category = category;
    }

    public string Name { get; set; }

    public string Category { get; set; }

    public string OperationSet { get; set; }

    public List<Category> Categories { get; set; }

    public object SourceObject { get; set; }

    public void AddProperty(Property property, string category)
    {
        Guard.NotNull(property, nameof(property));

        var cat = FindOrCreateCategory(category);
        cat.Properties.Add(property);
    }

    public Category FindOrCreateCategory(string category)
    {
        var cat = Categories.FindByName(category);
        if (cat == null)
        {
            cat = new Category(category);
            Categories.Add(cat);
        }

        return cat;
    }

    public Property GetProperty(string name, string category = null)
    {
        var cat = Categories.FindByName(category);
        return cat?.Properties.FindByName(name);
    }
}
