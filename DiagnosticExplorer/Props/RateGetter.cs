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

namespace DiagnosticExplorer;

internal class RateGetter : PropertyGetter
{
    private readonly bool _exposeRate = true;
    private readonly bool _exposeTotal = false;

    public RateGetter(PropertyInfo prop, RatePropertyAttribute attr, bool isStatic) : base(prop, isStatic)
    {
        if (attr != null)
        {
            _exposeRate = attr.ExposeRate;
            _exposeTotal = attr.ExposeTotal;
            Description = attr.Description;
        }
    }

    public override void GetProperties(object obj, PropertyBag bag, string catPrepend)
    {
        RateCounter rateCounter;
        try
        {
            rateCounter = (RateCounter) GetFunc(obj);
        }
        catch (Exception ex)
        {
            // A throwing rate property must degrade to an error string (like the guarded
            // PropertyGetter.GetValue path) rather than abort the whole diagnostic walk.
            string error = $"<{ex.Message}>";
            if (_exposeRate)
            {
                bag.AddProperty(new Property(Name + "/sec", error, Description) { SourceProperty = PropInfo }, PrependToCategory(catPrepend));
            }

            if (_exposeTotal)
            {
                bag.AddProperty(new Property("Total " + Name, error) { SourceProperty = PropInfo }, PrependToCategory(catPrepend));
            }

            return;
        }

        if (_exposeRate)
        {
            double? rate = rateCounter == null ? (double?) null : rateCounter.Rate;
            string val = rate == null ? "" : rate.Value.ToString("N2");
            Property property = new Property(Name + "/sec", val, Description)
            {
                SourceProperty = PropInfo,
                SourceObject = rateCounter,
                ValueObject = rate
            };

            bag.AddProperty(property, PrependToCategory(catPrepend));
        }

        if (_exposeTotal)
        {
            ulong? total = rateCounter == null ? (ulong?) null : rateCounter.Total;
            string val = total == null ? "" : total.ToString();
            Property property = new Property("Total " + Name, val)
            {
                SourceProperty = PropInfo,
                SourceObject = rateCounter,
                ValueObject = total
            };

            bag.AddProperty(property, PrependToCategory(catPrepend));
        }
    }
}