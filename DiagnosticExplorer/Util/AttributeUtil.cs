using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace DiagnosticExplorer.Util;

public static class AttributeUtil
{
    public static T GetAttribute<T>(PropertyInfo info) where T : Attribute
    {
        return info.GetCustomAttributes(typeof(T), false).Cast<T>().FirstOrDefault();
    }

    public static T GetAttribute<T>(Type type) where T : Attribute
    {
        return type.GetCustomAttributes(typeof(T), false).Cast<T>().FirstOrDefault();
    }

    public static T GetAttribute<T>(MethodInfo method) where T : Attribute
    {
        return method.GetCustomAttributes(typeof(T), false).Cast<T>().FirstOrDefault();
    }

}
