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
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using DiagnosticExplorer.Interface;
using DiagnosticExplorer.Util;

namespace DiagnosticExplorer.Props;

internal class PropertyGetter
{
    public const int MaxConcatItems = 10;

    protected PropertyGetter() { }

    public PropertyGetter(PropertyInfo propInfo, bool isStatic)
    {
        PropInfo = propInfo;

        GetFunc = PropertyToFunction(propInfo, isStatic);
        Name = propInfo.Name;

        CategoryAttribute catAttr = AttributeUtil.GetAttribute<CategoryAttribute>(propInfo);
        if (catAttr != null)
        {
            Category = catAttr.Category;
        }

        DescriptionAttribute descAttr = AttributeUtil.GetAttribute<DescriptionAttribute>(propInfo);
        if (descAttr != null)
        {
            Description = descAttr.Description;
        }

        Type declaringType =
            propInfo.DeclaringType
            ?? throw new ArgumentException(
                "Property must have a declaring type.",
                nameof(propInfo)
            );
        DiagnosticClassAttribute classAttr = declaringType
            .GetCustomAttributes(typeof(DiagnosticClassAttribute), true)
            .Cast<DiagnosticClassAttribute>()
            .FirstOrDefault();

        if (classAttr != null && classAttr.AllPropertiesSettable)
        {
            CanSet = propInfo.CanWrite && classAttr.AllPropertiesSettable;
        }

        PropertyAttribute propAttr = AttributeUtil.GetAttribute<PropertyAttribute>(propInfo);
        if (propAttr != null)
        {
            Name = propAttr.Name ?? Name;
            Category = propAttr.Category ?? Category;
            Description = propAttr.Description ?? Description;
            FormatString = propAttr.FormatString ?? GetDefaultFormatString(propInfo.PropertyType);
            if (propInfo.CanWrite && propAttr.AllowSetSpecified)
            {
                CanSet = propAttr.AllowSet;
            }
        }
    }

    protected static Func<object, object> PropertyToFunction(PropertyInfo propInfo, bool isStatic)
    {
        if (propInfo == null)
        {
            return null;
        }

        try
        {
            if (isStatic)
            {
                return obj => propInfo.GetValue(obj, null);
            }

            Type declaringType =
                propInfo.DeclaringType
                ?? throw new ArgumentException(
                    "Property must have a declaring type.",
                    nameof(propInfo)
                );
            ParameterExpression objParam = Expression.Parameter(typeof(object), "obj");
            UnaryExpression objToType = Expression.Convert(objParam, declaringType);
            Expression propExp = Expression.Property(objToType, propInfo);
            Expression resultToObj = Expression.Convert(propExp, typeof(object));
            return (Func<object, object>)Expression.Lambda(resultToObj, objParam).Compile();
        }
        catch (Exception ex)
        {
            string msg = string.Format(
                "Property {0}.{1}: {2}",
                propInfo.DeclaringType?.Name ?? "<unknown>",
                propInfo.Name,
                ex.Message
            );
            return _ => msg;
        }
    }

    protected Func<object, object> GetFunc { get; set; }

    protected static string MaxLengthString(string s, int maxLength)
    {
        if (s == null)
        {
            // ReSharper disable once ExpressionIsAlwaysNull -- legacy callers may supply null.
            return s;
        }

        if (s.Length <= maxLength)
        {
            return s;
        }

        return s.Substring(0, maxLength);
    }

    public virtual void GetProperties(object obj, PropertyBag bag, string catPrepend)
    {
        Property p = new Property
        {
            Name = Name,
            Description = Description,
            Value = MaxLengthString(GetValue(obj, out object objectValue), 8092),
            ValueObject = objectValue,
            CanSet = CanSet,
            SourceObject = obj,
            SourceProperty = PropInfo,
        };

        string prependToCategory = PrependToCategory(catPrepend);
        bag.AddProperty(p, prependToCategory);
    }

    protected string PrependToCategory(string prepend)
    {
        return CombineCategories(prepend, Category);
    }

    protected static string CombineCategories(string start, string end)
    {
        if (string.IsNullOrEmpty(start))
        {
            return end;
        }

        if (string.IsNullOrEmpty(end))
        {
            return start;
        }

        return start + "." + end;
    }

    private static string GetDefaultFormatString(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            type = type.GetGenericArguments()[0];
        }

        if (type == typeof(float))
        {
            return "{0:N2}";
        }

        if (type == typeof(double))
        {
            return "{0:N2}";
        }

        if (type == typeof(decimal))
        {
            return "{0:N2}";
        }

        if (type == typeof(DateTime) || type == typeof(DateTime?))
        {
            return "{0:d MMM yyyy H:mm:ss}";
        }

        return null;
    }

    public PropertyInfo PropInfo { get; private set; }
    public string Name { get; protected set; }
    public string Description { get; protected set; }
    protected string FormatString { get; private set; }
    public bool CanSet { get; private set; }
    public string Category { get; private set; }

    [SuppressMessage(
        "Maintainability",
        "S3776:Cognitive Complexity of methods should not be too high",
        Justification = "Bounded enumeration handles counted and streaming collections in one pass."
    )]
    protected string FormatEnumerable(IEnumerable col, string separator, int maxItems)
    {
        if (maxItems <= 0)
        {
            maxItems = MaxConcatItems;
        }

        int count = -1;
        if (col is ICollection c)
        {
            count = c.Count;
        }
        else
        {
            PropertyInfo countProp = col.GetType()
                .GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
            if (
                countProp != null
                && countProp.PropertyType == typeof(int)
                && countProp.GetValue(col) is int propertyCount
            )
            {
                count = propertyCount;
            }
        }

        if (count == 0)
        {
            return "0 items";
        }

        List<object> asObject;
        int remaining;
        int displayCount;

        if (count != -1)
        {
            asObject = col.Cast<object>().Take(maxItems).ToList();
            remaining = count - asObject.Count;
            displayCount = count;
        }
        else
        {
            asObject = col.Cast<object>().Take(maxItems + 1).ToList();
            if (asObject.Count > maxItems)
            {
                remaining = 1;
                asObject.RemoveAt(maxItems);
                displayCount = maxItems;
            }
            else
            {
                remaining = 0;
                displayCount = asObject.Count;
            }
        }

        if (displayCount == 0)
        {
            return "0 items";
        }

        List<string> values = [];
        foreach (object o in asObject)
        {
            values.Add(FormatValue(o));
        }

        if (remaining > 0)
        {
            if (count != -1)
            {
                values.Add(
                    string.Format("... ({0} more item{1})", remaining, remaining == 1 ? "" : "s")
                );
            }
            else
            {
                values.Add("... (more items)");
            }
        }

        string suffix = count == 1 ? "" : "s";
        string pre = count != -1 ? string.Format("{0} item{1}: ", count, suffix) : "Many items: ";
        return pre + string.Join(separator, values.ToArray());
    }

    public string GetValue(object obj, out object objectValue)
    {
        return GetValue(obj, GetFunc, out objectValue);
    }

    public string GetValue(object obj, Func<object, object> propInfo, out object propertyValue)
    {
        try
        {
            propertyValue = propInfo(obj);
            if (propertyValue == null)
            {
                return null;
            }

            return FormatValue(propertyValue);
        }
        catch (Exception ex)
        {
            propertyValue = null;
            return string.Format("<{0}>", ex.Message);
        }
    }

    protected string FormatValue(object val)
    {
        if (val == null)
        {
            return null;
        }

        if (val is TimeSpan timeSpan)
        {
            return FormatTimeSpan(timeSpan);
        }

        if (val is string str)
        {
            return str;
        }

        if (val is IEnumerable enumerable)
        {
            return FormatEnumerable(enumerable, Environment.NewLine, MaxConcatItems);
        }

        if (FormatString != null)
        {
            return string.Format(FormatString, val);
        }

        return val.ToString();
    }

    protected static string FormatTimeSpan(TimeSpan span)
    {
        string sign = span < TimeSpan.Zero ? "-" : "";
        string format = "{0}{2:D2}:{3:D2}:{4:D2}";
        if (span.Days != 0)
        {
            format = "{0}{1}.{2:D2}:{3:D2}:{4:D2}";
        }

        if (Math.Abs(span.TotalSeconds) < 1)
        {
            format += ".{5:D2}";
        }

        return string.Format(
            format,
            sign,
            Math.Abs(span.Days),
            Math.Abs(span.Hours),
            Math.Abs(span.Minutes),
            Math.Abs(span.Seconds),
            Math.Abs(span.Milliseconds)
        );
    }
}
