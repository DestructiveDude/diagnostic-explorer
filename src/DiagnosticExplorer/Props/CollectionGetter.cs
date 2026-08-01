using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using DiagnosticExplorer.Interface;

namespace DiagnosticExplorer.Props;

internal class CollectionGetter : PropertyGetter
{
    private readonly string _separator;
    private readonly CollectionMode _mode;
    private readonly int _maxItems;
    private readonly Func<object, object> _nameFunc;
    private readonly Func<object, object> _valueFunc;
    private readonly Func<object, object> _descrFunc;
    private readonly Func<object, object> _catFunc;

    public CollectionGetter(PropertyInfo info, CollectionPropertyAttribute attr, bool isStatic)
        : base(info, isStatic)
    {
        _separator = attr.Separator ?? Environment.NewLine;
        _mode = attr.Mode;

        Type genericType = GenericObjectCache.FindGenericInterface(info.PropertyType, typeof(IDictionary<,>));
        bool isDictionary = typeof(IDictionary).IsAssignableFrom(info.PropertyType);

        if (genericType != null)
        {
            IDictPropGetter propGetter = GenericObjectCache.CreateGenericObject<IDictPropGetter>(
                typeof(DictPropGetter<,>),
                genericType.GetGenericArguments()
            );
            _nameFunc = propGetter.GetNameGetter();
            _valueFunc = propGetter.GetValueGetter();
        }
        else if (isDictionary)
        {
            _nameFunc = x => ((DictionaryEntry)x).Key;
            _valueFunc = x => ((DictionaryEntry)x).Value;
        }
        else
        {
            _nameFunc = PropertyToFunction(GetListProperty(info, attr.NameProperty), isStatic);
            _valueFunc = PropertyToFunction(GetListProperty(info, attr.ValueProperty), isStatic);
            _descrFunc = PropertyToFunction(GetListProperty(info, attr.DescriptionProperty), isStatic);
            _catFunc = PropertyToFunction(GetListProperty(info, attr.CategoryProperty), isStatic);
        }
        _maxItems = attr.MaxItems;
    }

    public interface IDictPropGetter
    {
        Func<object, object> GetNameGetter();
        Func<object, object> GetValueGetter();
    }

    public sealed class DictPropGetter<TKey, TValue> : IDictPropGetter
    {
        public Func<object, object> GetNameGetter()
        {
            return value => ((KeyValuePair<TKey, TValue>)value).Key;
        }

        public Func<object, object> GetValueGetter()
        {
            return value => ((KeyValuePair<TKey, TValue>)value).Value;
        }
    }

    private static PropertyInfo GetListProperty(PropertyInfo info, string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        Type colType = null;
        if (info.PropertyType.IsArray)
        {
            colType = info.PropertyType.GetElementType();
        }
        else
        {
            Type enumerableType = GenericObjectCache.FindGenericInterface(info.PropertyType, typeof(IEnumerable<>));
            if (enumerableType != null)
            {
                colType = enumerableType.GetGenericArguments()[0];
            }
        }

        if (colType == null)
        {
            return null;
        }

        PropertyInfo propInfo = colType.GetProperty(name, DiagnosticManager.PublicInstancePropertyFlags);

        if (propInfo == null)
        {
            Debug.WriteLine($"Diagnostics: Can't find property '{name}' on class '{colType}'");
        }

        return propInfo;
    }

    [SuppressMessage(
        "Maintainability",
        "S3776:Cognitive Complexity of methods should not be too high",
        Justification = "The collection modes share bounded enumeration and property projection."
    )]
    public override void GetProperties(object obj, PropertyBag bag, string catPrepend)
    {
        try
        {
            if (GetFunc(obj) is not IEnumerable rawCol)
            {
                bag.AddProperty(new Property(Name, null), PrependToCategory(catPrepend));
                return;
            }

            int count = -1;
            if (rawCol is ICollection c)
            {
                count = c.Count;
            }
            else
            {
                PropertyInfo countProp = rawCol
                    .GetType()
                    .GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
                if (
                    countProp != null
                    && countProp.PropertyType == typeof(int)
                    && countProp.GetValue(rawCol) is int propertyCount
                )
                {
                    count = propertyCount;
                }
            }

            if (_mode == CollectionMode.Count && count != -1)
            {
                bag.AddProperty(new Property(Name, FormatValue(count)), PrependToCategory(catPrepend));
                return;
            }

            List<object> col = rawCol.Cast<object>().Take(10001).ToList();
            int actualCount = col.Count;
            bool wasTruncated = actualCount > 10000;
            if (wasTruncated)
            {
                col.RemoveAt(10000);
            }

            int displayCount = count != -1 ? count : col.Count;

            if (displayCount == 0)
            {
                bag.AddProperty(new Property(Name, FormatValue(0)), PrependToCategory(catPrepend));
                return;
            }

            switch (_mode)
            {
                case CollectionMode.Count:
                    string val = wasTruncated ? "10000+ items" : FormatValue(displayCount);
                    bag.AddProperty(new Property(Name, val), PrependToCategory(catPrepend));
                    break;
                case CollectionMode.Concatenate:
                    AppendConcatenated(col, bag, catPrepend);
                    break;
                case CollectionMode.List:
                    AppendAllProperties(col, bag, catPrepend);
                    if (wasTruncated)
                    {
                        bag.AddProperty(new Property("...", "Truncated at 10000 items"), PrependToCategory(catPrepend));
                    }
                    break;
                case CollectionMode.Categories:
                    AppendSeparateCategories(col, bag, catPrepend);
                    if (wasTruncated)
                    {
                        bag.AddProperty(
                            new Property("...", "Truncated at 10000 items"),
                            CombineCategories(catPrepend, "...")
                        );
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            string error = $"<{ex.Message}>";
            bag.AddProperty(new Property(Name, error), PrependToCategory(catPrepend));
        }
    }

    [SuppressMessage(
        "Maintainability",
        "S3776:Cognitive Complexity of methods should not be too high",
        Justification = "Cycle detection and recursive diagnostic projection are intentionally colocated."
    )]
    private void AppendSeparateCategories(IEnumerable col, PropertyBag bag, string catPrepend)
    {
        int index = 0;
        foreach (object listObject in col)
        {
            if (listObject == null)
            {
                continue;
            }

            var visited = DiagnosticManager.VisitedObjects;
            if (visited.Contains(listObject))
            {
                Property p = new Property
                {
                    Name = "<cycle>",
                    CanSet = false,
                    SourceObject = listObject,
                };
                string cycleCategory = $"Item {index++}";
                string cyclePrepend = CombineCategories(catPrepend, cycleCategory);
                bag.AddProperty(p, cyclePrepend);
                continue;
            }
            if (visited.Count > 50)
            {
                Property p = new Property
                {
                    Name = "<max depth>",
                    CanSet = false,
                    SourceObject = listObject,
                };
                string depthCategory = $"Item {index++}";
                string depthPrepend = CombineCategories(catPrepend, depthCategory);
                bag.AddProperty(p, depthPrepend);
                continue;
            }

            object catPropVal = GetNextPropVal(listObject, _catFunc ?? _nameFunc, index++);
            string valCategory = Convert.ToString(catPropVal);
            if (!string.IsNullOrEmpty(Category))
            {
                valCategory = Category.Contains('{')
                    ? string.Format(Category, catPropVal)
                    : $"{Category}.{valCategory}";
            }

            string newPrepend = CombineCategories(catPrepend, valCategory);

            visited.Add(listObject);
            try
            {
                foreach (PropertyGetter getter in DiagnosticManager.GetPropertyGetters(listObject))
                {
                    getter.GetProperties(listObject, bag, newPrepend);
                }

                if (bag.Categories.FindByName(newPrepend) is Category cat)
                {
                    cat.ValueObject = listObject;
                }
            }
            finally
            {
                visited.Remove(listObject);
            }
        }
    }

    private void AppendAllProperties(IEnumerable col, PropertyBag bag, string catPrepend)
    {
        int index = 0;
        foreach (object obj in col)
        {
            object objectValue = obj;
            string name = Convert.ToString(GetNextPropVal(obj, _nameFunc, index++));
            string val = _valueFunc == null ? FormatValue(obj) : GetValue(obj, _valueFunc, out objectValue);

            string desc = _descrFunc == null ? null : GetValue(obj, _descrFunc, out _);
            string cat = _catFunc == null ? null : GetValue(obj, _catFunc, out _);

            Property prop = new Property(name, val, desc) { ValueObject = objectValue };
            bag.AddProperty(prop, CombineCategories(PrependToCategory(catPrepend), cat));
        }
    }

    private void AppendConcatenated(IEnumerable col, PropertyBag bag, string catPrepend)
    {
        if (_valueFunc != null)
        {
            col = col.Cast<object>()
                .Select(item =>
                {
                    try
                    {
                        return _valueFunc(item);
                    }
                    catch (Exception ex)
                    {
                        return $"<{ex.Message}>";
                    }
                });
        }

        string val = FormatEnumerable(col, _separator, _maxItems);
        bag.AddProperty(new Property(Name, val), PrependToCategory(catPrepend));
    }

    private object GetNextPropVal(object obj, Func<object, object> propFunc, int index)
    {
        if (propFunc == null)
        {
            return $"{Name} {index}";
        }

        try
        {
            return propFunc(obj);
        }
        catch (Exception ex)
        {
            return $"<{ex.Message}>";
        }
    }
}
