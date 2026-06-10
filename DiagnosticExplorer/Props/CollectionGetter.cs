using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace DiagnosticExplorer;

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
        bool isDictionary = info.PropertyType.GetInterfaces().Contains(typeof(IDictionary));

        if (genericType != null)
        {
            DictPropGetter propGetter = GenericObjectCache.CreateGenericObject<DictPropGetter>(typeof(DictPropGetter<,>),
                genericType.GetGenericArguments());
            _nameFunc = propGetter.GetNameGetter();
            _valueFunc = propGetter.GetValueGetter();
        }
        else if (isDictionary)
        {
            _nameFunc = x => ((DictionaryEntry) x).Key;
            _valueFunc = x => ((DictionaryEntry) x).Value;
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

    public abstract class DictPropGetter
    {
        public abstract Func<object, object> GetNameGetter();
        public abstract Func<object, object> GetValueGetter();
    }

    public class DictPropGetter<TKey, TValue> : DictPropGetter
    {
        public override Func<object, object> GetNameGetter()
        {
            return value => ((KeyValuePair<TKey, TValue>) value).Key;
        }

        public override Func<object, object> GetValueGetter()
        {
            return value => ((KeyValuePair<TKey, TValue>) value).Value;
        }
    }

    private PropertyInfo GetListProperty(PropertyInfo info, string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (!info.PropertyType.IsGenericType)
        {
            return null;
        }

        if (info.PropertyType.GetGenericArguments().Length != 1)
        {
            return null;
        }

        Type colType = info.PropertyType.GetGenericArguments()[0];
        PropertyInfo propInfo = colType.GetProperty(name, DiagnosticManager.PublicInstancePropertyFlags);

        if (propInfo == null)
        {
            Debug.WriteLine($"Diagnostics: Can't find property '{name}' on class '{colType}'");
        }

        return propInfo;
    }

    public override void GetProperties(object obj, PropertyBag bag, string catPrepend)
    {
        try
        {
            if (GetFunc(obj) is not IEnumerable rawCol)
            {
                bag.AddProperty(new Property(Name, null), PrependToCategory(catPrepend));
                return;
            }

            // Materialize once: the count below and each mode branch would otherwise each
            // re-enumerate the source (concatenate mode up to three passes via FormatEnumerable),
            // re-running stateful/expensive/single-pass sequences and doubling side effects.
            List<object> col = rawCol.Cast<object>().ToList();

            int count = col.Count;

            if (count == 0)
            {
                bag.AddProperty(new Property(Name, FormatValue(count)), PrependToCategory(catPrepend));
                return;
            }

            switch (_mode)
            {
                case CollectionMode.Count:
                    {
                        bag.AddProperty(new Property(Name, FormatValue(count)), PrependToCategory(catPrepend));
                        break;
                    }
                case CollectionMode.Concatenate:
                    {
                        AppendConcatenated(col, bag, catPrepend);
                        break;
                    }
                case CollectionMode.List:
                    AppendAllProperties(col, bag, catPrepend);
                    break;
                case CollectionMode.Categories:
                    AppendSeparateCategories(col, bag, catPrepend);
                    break;
            }
        }
        catch (Exception ex) // May get exception if the collection is modified during iteration
        {
            string error = $"<{ex.Message}>";
            bag.AddProperty(new Property(Name, error), PrependToCategory(catPrepend));
        }
    }

    private void AppendSeparateCategories(IEnumerable col, PropertyBag bag, string catPrepend)
    {
        int index = 0;
        foreach (object listObject in col)
        {
            object catPropVal = GetNextPropVal(listObject, _catFunc, index++);
            string valCategory = Convert.ToString(catPropVal);
            if (!string.IsNullOrEmpty(Category))
            {
                valCategory = Category.IndexOf("{") != -1 ? string.Format(Category, catPropVal) : $"{Category}.{valCategory}";
            }

            string newPrepend = CombineCategories(catPrepend, valCategory);

            foreach (PropertyGetter getter in DiagnosticManager.GetPropertyGetters(listObject))
            {
                getter.GetProperties(listObject, bag, newPrepend);
            }

            Category cat = bag.Categories.FindByName(newPrepend);
            if (cat != null)
            {
                cat.ValueObject = listObject;
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
            string desc = _descrFunc == null ? null : GetValue(obj, _descrFunc, out objectValue);
            string cat = _catFunc == null ? null : GetValue(obj, _catFunc, out objectValue);

            Property prop = new Property(name, val, desc)
            {
                ValueObject = objectValue
            };
            bag.AddProperty(prop, CombineCategories(PrependToCategory(catPrepend), cat));
        }
    }

    private void AppendConcatenated(IEnumerable col, PropertyBag bag, string catPrepend)
    {
        if (_valueFunc != null)
        {
            col = col.Cast<object>().Select(_valueFunc);
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

        return propFunc(obj);
    }
}