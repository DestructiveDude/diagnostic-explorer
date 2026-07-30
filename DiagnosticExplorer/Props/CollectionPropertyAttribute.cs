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

namespace DiagnosticExplorer.Props;

public enum CollectionMode
{
    /// <summary>The count of a collection property is exposed</summary>
    Count,

    /// <summary>The items in a collection property are concatenated together</summary>
    Concatenate,

    /// <summary>The items in a collection property are listed individually</summary>
    List,

    /// <summary>Each item in a collection property is exposed in its own category</summary>
    Categories,
}

[AttributeUsage(AttributeTargets.Property)]
public class CollectionPropertyAttribute : PropertyAttribute
{
    public CollectionPropertyAttribute(CollectionMode mode)
        : this(mode, null) { }

    public CollectionPropertyAttribute(CollectionMode mode, string displayName)
        : this(mode, displayName, null) { }

    public CollectionPropertyAttribute(CollectionMode mode, string displayName, string category)
        : base(displayName, category)
    {
        Mode = mode;
        MaxItems = PropertyGetter.MaxConcatItems;
    }

    public CollectionMode Mode { get; set; }
    public string NameProperty { get; set; }
    public string ValueProperty { get; set; }
    public string DescriptionProperty { get; set; }
    public string CategoryProperty { get; set; }
    public string Separator { get; set; }
    public int MaxItems { get; set; }
}
