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
using System.Linq;

namespace DiagnosticExplorer;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class PropertyAttribute : Attribute
{
    public PropertyAttribute() { }

    public PropertyAttribute(string name) : this(name, null)
    {
    }

    public PropertyAttribute(string name, string category) : this(name, category, null)
    {

    }

    public PropertyAttribute(string name, string category, string description)
    {
        Ignore = false;
        Name = name;
        Category = category;
        Description = description;
    }

    public bool Ignore { get; set; }

    public string Name { get; set; }

    public string FormatString { get; set; }

    public string Category { get; set; }

    public string Description { get; set; }

    private bool _allowSet;
    public bool AllowSet
    {
        get => _allowSet;
        set {
            _allowSet = value;
            AllowSetSpecified = true;
        }
    }

    internal bool AllowSetSpecified { get; private set; }
}