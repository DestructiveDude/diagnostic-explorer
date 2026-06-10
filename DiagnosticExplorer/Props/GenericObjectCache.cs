using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DiagnosticExplorer;

internal static class GenericObjectCache
{
    // Ordinal comparer: keys are Type.FullName concatenations, so culture-sensitive
    // comparison (Turkish-I) could fold distinct type keys together. ConcurrentDictionary
    // makes the check-then-set atomic on the thread-pool getter-build path.
    private static readonly ConcurrentDictionary<string, object> _objectCache = new(StringComparer.Ordinal);

    public static Type FindGenericInterface(Type targetType, Type interfaceType)
    {
        IEnumerable<Type> candidates = Enumerable.Repeat(targetType, 1)
            .Concat(targetType.GetInterfaces());

        foreach (Type candidate in candidates)
        {
            if (interfaceType.IsGenericType)
            {
                if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == interfaceType)
                {
                    return candidate;
                }
            }
            else
            {
                if (candidate == interfaceType)
                {
                    return candidate;
                }
            }
        }
        return null;
    }


    public static T CreateGenericObject<T>(Type genericType, params Type[] typeArguments)
    {
        string key = string.Format("{0} {1}",
                                                             genericType.AssemblyQualifiedName,
                                                             string.Join(", ", typeArguments.Select(x => x.AssemblyQualifiedName).ToArray()));

        return (T) _objectCache.GetOrAdd(key, _ => {
            Type type = genericType.MakeGenericType(typeArguments);
            return Activator.CreateInstance(type);
        });
    }
}
