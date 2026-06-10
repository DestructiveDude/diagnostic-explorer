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
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml.Serialization;
using ProtoBuf;

namespace DiagnosticExplorer;

[ProtoContract(UseProtoMembersOnly = true)]
public class Property
{
    public Property()
    {
    }

    public Property(string name)
        : this(name, null, null)
    {
    }

    public Property(string name, string value) : this(name, value, null)
    {
    }

    public Property(string name, string value, string description)
    {
        Name = name;
        Value = value;
        Description = description;
    }

    [ProtoMember(1)]
    public string Name { get; set; }

    [ProtoMember(2)]
    public string Value { get; set; }

    [ProtoMember(3)]
    public string Description { get; set; }

    [ProtoMember(4)]
    public string OperationSet { get; set; }

    [ProtoMember(5)]
    public bool CanSet { get; set; }

    internal object SourceObject { get; set; }

    internal object ValueObject { get; set; }

    internal PropertyInfo SourceProperty { get; set; }

    public override string ToString()
    {
        string descr = string.IsNullOrEmpty(Description) ? "" : string.Format(" ({0})", Description);

        string opset = OperationSet == null ? "" : string.Format(" (OperationSet={0})", OperationSet);

        string settable = CanSet ? " (SET)" : "";

        return $"{Name} = [{Value}]{settable}{descr}{opset}";
    }
}

public static class PropertyExtensions
{
    private static readonly StringComparer _ignoreCase = StringComparer.CurrentCultureIgnoreCase;

    public static Property FindByName(this IEnumerable<Property> list, string name)
    {
        if (list == null)
        {
            throw new ArgumentNullException(nameof(list));
        }

        return list.FirstOrDefault(x => _ignoreCase.Equals(x.Name, name));
    }
}