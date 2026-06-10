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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using DiagnosticExplorer;
using ProtoBuf;

namespace DiagnosticExplorer;

[ProtoContract(UseProtoMembersOnly = true)]
public class PropertyBag
{

    public PropertyBag()
    {
        Categories = [];
    }

    public PropertyBag(string name) : this()
    {
        Name = name;
    }

    public PropertyBag(string name, string category) : this(name)
    {
        Category = category;
    }

    public void AddProperty(Property property, string category)
    {
        if (property == null)
        {
            throw new ArgumentNullException(nameof(property));
        }

        Category cat = FindOrCreateCategory(category);
        cat.Properties.Add(property);
    }

    public Category FindOrCreateCategory(string category)
    {
        Category cat = Categories.FindByName(category);
        if (cat == null)
        {
            cat = new Category(category);
            Categories.Add(cat);
        }
        return cat;
    }


    [ProtoMember(1)]
    public string Name { get; set; }

    [ProtoMember(2)]
    public string Category { get; set; }

    [ProtoMember(3)]
    public string OperationSet { get; set; }

    [ProtoMember(4)]
    public List<Category> Categories { get; set; }

    public object SourceObject { get; set; }

    public Property GetProperty(string name, string category = null)
    {
        Category cat = Categories.FindByName(category);
        return cat?.Properties.FindByName(name);
    }
}