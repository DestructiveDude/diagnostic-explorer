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
using System.Reflection;
using DiagnosticExplorer.Interface;

namespace DiagnosticExplorer.Props;

internal class ExtendedPropertyGetter : PropertyGetter
{
    private readonly string _name;

    public ExtendedPropertyGetter(PropertyInfo info, ExtendedPropertyAttribute attr, bool isStatic)
        : base(info, isStatic)
    {
        _name = attr.Name ?? info.Name;
    }

    public override void GetProperties(object obj, PropertyBag bag, string catPrepend)
    {
        string newPrepend = CombineCategories(catPrepend, _name);

        object val;
        try
        {
            val = GetFunc(obj);
        }
        catch (Exception ex)
        {
            Property p = new Property
            {
                Name = "Error",
                Value = $"<{ex.InnerException?.Message ?? ex.Message}>",
                CanSet = false,
                SourceObject = obj,
                SourceProperty = PropInfo
            };
            string prependToCategory = PrependToCategory(newPrepend);
            bag.AddProperty(p, prependToCategory);
            return;
        }

        if (val == null)
        {
            Property p = new Property
            {
                Name = "null",
                CanSet = CanSet,
                SourceObject = obj,
                SourceProperty = PropInfo
            };

            string prependToCategory = PrependToCategory(newPrepend);
            bag.AddProperty(p, prependToCategory);
        }
        else
        {
            List<PropertyGetter> getters = DiagnosticManager.GetPropertyGetters(val);
            foreach (PropertyGetter getter in getters)
            {
                getter.GetProperties(val, bag, newPrepend);
            }
            Category cat = bag.Categories.FindByName(newPrepend);
            cat?.ValueObject = val;
        }
    }
}