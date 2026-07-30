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
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DiagnosticExplorer.Interface;

namespace DiagnosticExplorer.Props;

internal class DateGetter : PropertyGetter
{
    private readonly bool _exposeDate = true;
    private readonly bool _exposeElapsed;
    private readonly bool _exposeTimeUntil;
    private readonly bool _isUtc;

    public DateGetter(PropertyInfo prop, DatePropertyAttribute attr, bool isStatic)
        : base(prop, isStatic)
    {
        if (attr != null)
        {
            _exposeDate = attr.ExposeDate;
            _exposeElapsed = attr.ExposeElapsed;
            _exposeTimeUntil = attr.ExposeTimeUntil;
            _isUtc = attr.IsUTC;
        }
    }

    [SuppressMessage(
        "Maintainability",
        "S3776:Cognitive Complexity of methods should not be too high",
        Justification = "The branches are the independent date, elapsed and time-until presentation options."
    )]
    [SuppressMessage(
        "CodeQuality",
        "S6561:Avoid DateTime.Now for elapsed time",
        Justification = "This compares display-oriented local calendar values and is not a benchmark or duration clock."
    )]
    public override void GetProperties(object obj, PropertyBag bag, string catPrepend)
    {
        if (_exposeDate)
        {
            base.GetProperties(obj, bag, catPrepend);
        }

        if (!_exposeElapsed && !_exposeTimeUntil)
        {
            return;
        }

        DateTime? dateVal;
        try
        {
            var value = GetFunc(obj);
            dateVal = value is DateTimeOffset off ? off.LocalDateTime : (DateTime?)value;
            if (dateVal != null)
            {
                if (_isUtc && dateVal.Value.Kind == DateTimeKind.Unspecified)
                {
                    dateVal = DateTime.SpecifyKind(dateVal.Value, DateTimeKind.Utc).ToLocalTime();
                }
                else if (dateVal.Value.Kind == DateTimeKind.Utc)
                {
                    dateVal = dateVal.Value.ToLocalTime();
                }
            }
        }
        catch (Exception ex)
        {
            // A throwing date property must degrade to an error string rather than abort the
            // whole diagnostic walk; this raw getter call bypassed the guarded GetValue path.
            var error = $"<{ex.Message}>";
            if (_exposeElapsed)
            {
                bag.AddProperty(
                    new Property("Time since " + Name, error),
                    PrependToCategory(catPrepend)
                );
            }

            if (_exposeTimeUntil)
            {
                bag.AddProperty(
                    new Property("Time until " + Name, error),
                    PrependToCategory(catPrepend)
                );
            }

            return;
        }

        if (_exposeElapsed)
        {
            var val = dateVal == null ? "" : FormatTimeSpan(DateTime.Now.Subtract(dateVal.Value));
            var property = new Property("Time since " + Name, val);
            bag.AddProperty(property, PrependToCategory(catPrepend));
        }

        if (_exposeTimeUntil)
        {
            var val = dateVal == null ? "" : FormatTimeSpan(dateVal.Value.Subtract(DateTime.Now));
            var property = new Property("Time until " + Name, val);
            bag.AddProperty(property, PrependToCategory(catPrepend));
        }
    }
}
