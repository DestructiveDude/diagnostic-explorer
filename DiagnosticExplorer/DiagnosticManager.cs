using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using DiagnosticExplorer.Util;

namespace DiagnosticExplorer
{
	public static class DiagnosticManager
	{
		private static readonly StringComparer _ignoreCase = StringComparer.CurrentCultureIgnoreCase;
		private static List<RegisteredObject> RegisteredObjects { get; set; }

		private static readonly ConcurrentDictionary<string, List<PropertyGetter>> _typeHash = new();

		private static readonly ConcurrentDictionary<Type, Lazy<OperationSet>> _operationLookup = new();
		private static readonly ConcurrentDictionary<Type, Lazy<OperationSet>> _staticOperationLookup = new();
		private static int _operationSetId;
        public static bool Enabled { get; set; } = true;

		static DiagnosticManager()
		{
			RegisteredObjects = new List<RegisteredObject>();
		}

		internal static void Clear()
		{
			_operationLookup.Clear();
			_staticOperationLookup.Clear();
			_typeHash.Clear();
			lock (RegisteredObjects)
				RegisteredObjects.Clear();
		}

		public static void Register(object o, string bagName, string bagCategory)
        {
            if (!Enabled) return;

			lock (RegisteredObjects)
			{
				RegisteredObject existing = RegisteredObjects.Find(ro => ReferenceEquals(ro.Object, o));

                bagName = MakeNameUnique(existing, bagName, bagCategory);
				if (existing == null)
				{
					RegisteredObjects.Add(new RegisteredObject(o, bagCategory, bagName));
				}
                else
                {
                    existing.BagName = bagName;
                    existing.BagCategory = bagCategory;
                }
			}
		}

        private static string MakeNameUnique(RegisteredObject obj, string name, string category)
        {
            if (name == null)
                return name;

            var takenNames = new HashSet<string>(_ignoreCase);
            foreach (var ro in RegisteredObjects.Where(ro => !ReferenceEquals(ro, obj) && _ignoreCase.Equals(category, ro.BagCategory)))
            {
                takenNames.Add(ro.BagName);
            }

            if (!takenNames.Contains(name))
                return name;

            for (int i = 2; ; i++)
            {
                string extension = $" {i}";
                string newName = $"{name}{extension}";
                if (!takenNames.Contains(newName))
                    return newName;
            }
        }


        public static void Unregister(object obj)
		{
			lock (RegisteredObjects)
			{
				RegisteredObject existing = RegisteredObjects.Find(
					ro => ReferenceEquals(ro.Object, obj));

				if (existing != null)
					RegisteredObjects.Remove(existing);
			}
		}

		public static DiagnosticResponse GetDiagnostics()
		{
			return GetDiagnostics(GetRegisteredObjects());
		}


		public static DiagnosticResponse GetDiagnostics(IEnumerable<RegisteredObject> registeredObjects)
		{
			try
			{
				DiagnosticResponse response = new();

				response.PropertyBags.AddRange(
					registeredObjects.Select(x => ObjectToPropertyBag(x.Object, x.BagName, x.BagCategory)));

				HashSet<OperationSet> operationSets = new();

				foreach (PropertyBag bag in response.PropertyBags)
				{
					OperationSet bagOperations = GetOperationSet(bag.SourceObject);
					if (bagOperations != null)
					{
						bag.OperationSet = bagOperations.Id;
						operationSets.Add(bagOperations);
					}

					foreach (Category cat in bag.Categories)
					{
						OperationSet catOperations = GetOperationSet(cat.ValueObject);
						if (catOperations != null)
						{
							cat.OperationSet = catOperations.Id;
							operationSets.Add(catOperations);
						}
					}

					foreach (Property prop in bag.Categories.SelectMany(x => x.Properties))
					{
						OperationSet propOperations = GetOperationSet(prop.ValueObject);
						if (propOperations != null)
						{
							prop.OperationSet = propOperations.Id;
							operationSets.Add(propOperations);
						}
					}
				}
				response.OperationSets.AddRange(operationSets);

				return response;
			}
			catch (Exception ex)
			{
				return new DiagnosticResponse
				{
					ExceptionMessage = ex.Message,
					ExceptionDetail = ex.ToString()
				};
			}
		}

		private static OperationSet GetOperationSet(object sourceObject)
		{
			if (sourceObject == null) return null;

			if (sourceObject is Type type)
			{
				Lazy<OperationSet> lazy = _staticOperationLookup.GetOrAdd(type, t => new Lazy<OperationSet>(() => BuildStaticOperationSet(t)));
				return lazy.Value;
			}

			Type propType = sourceObject.GetType();
			Lazy<OperationSet> lazyInstance = _operationLookup.GetOrAdd(propType, t => new Lazy<OperationSet>(() => BuildOperationSet(t)));
			return lazyInstance.Value;
		}

		private static OperationSet BuildOperationSet(Type propType)
		{
			OperationSet operationSet = CreateOperationSet(propType);
			if (operationSet != null)
				operationSet.Id = Interlocked.Increment(ref _operationSetId).ToString();
			return operationSet;
		}

		private static OperationSet BuildStaticOperationSet(Type propType)
		{
			OperationSet operationSet = CreateStaticOperationSet(propType);
			if (operationSet != null)
				operationSet.Id = Interlocked.Increment(ref _operationSetId).ToString();
			return operationSet;
		}

		private static OperationSet CreateOperationSet(Type propType)
		{
			if (propType == null) throw new ArgumentNullException(nameof(propType));

			if (propType.FullName == null) return null;
			if (propType.FullName.StartsWith("System.")) return null;

			OperationSet operationSet = new();

			foreach (MethodInfo method in propType.GetMethods(PublicMethods).OrderBy(x => x.Name))
			{
				if (IsMethodValidOperationTarget(method))
					operationSet.Operations.Add(new Operation(method));
			}

			return operationSet.Operations.Count == 0 ? null : operationSet;
		}

		private static OperationSet CreateStaticOperationSet(Type propType)
		{
			if (propType == null) throw new ArgumentNullException(nameof(propType));

			if (propType.FullName == null) return null;
			if (propType.FullName.StartsWith("System.")) return null;

			OperationSet operationSet = new();

			foreach (MethodInfo method in propType.GetMethods(PublicStaticMethods).OrderBy(x => x.Name).Where(IsMethodValidOperationTarget))
			{
				operationSet.Operations.Add(new Operation(method));
			}

			return operationSet.Operations.Count == 0 ? null : operationSet;
		}

		/// <summary>
		/// To be a valid operation target, a method must contain no ref/out parameters, 
		/// no generic parameters apart from Nullable, and the must be allowed either by the DiagnosticClassAttribute
		/// or DiagnosticMethodAttribute
		/// </summary>
		private static bool IsMethodValidOperationTarget(MethodInfo method)
		{
			if (method.IsSpecialName) return false;
			if (method.IsGenericMethod) return false;
			if (method.GetParameters().Any(x => x.IsOut)) return false;
			if (method.GetParameters().Any(x => x.ParameterType.IsByRef)) return false;

			return AttributeUtil.GetAttribute<DiagnosticMethodAttribute>(method) != null;
		}

		public static RegisteredObject[] GetRegisteredObjects()
		{
			List<RegisteredObject> list = new();

			lock (RegisteredObjects)
			{
				for (int i = RegisteredObjects.Count - 1; i >= 0; i--)
				{
					RegisteredObject obj = RegisteredObjects[i];
					if (obj.Object == null)
						RegisteredObjects.RemoveAt(i);
					else
						list.Add(obj);
				}
			}
			return list.ToArray();
		}


        public static PropertyBag ObjectToPropertyBag(object obj, string bagName, string bagCategory)
		{
			PropertyBag bag = new();
			bag.Name = bagName;
			bag.Category = bagCategory;
			bag.SourceObject = obj;

			List<PropertyGetter> valueGetters = GetPropertyGetters(obj);

			foreach (PropertyGetter getter in valueGetters)
				getter.GetProperties(obj, bag, null);

			return bag;
		}

		public const BindingFlags PublicInstancePropertyFlags = BindingFlags.Public | BindingFlags.GetProperty | BindingFlags.Instance;
		private const BindingFlags PublicStaticPropertyFlags = BindingFlags.Public | BindingFlags.GetProperty | BindingFlags.Static;
		private const BindingFlags PublicMethods = BindingFlags.Public | BindingFlags.InvokeMethod | BindingFlags.Instance;
		private const BindingFlags PublicStaticMethods = BindingFlags.Static | BindingFlags.InvokeMethod | BindingFlags.Public;


		internal static List<PropertyGetter> GetPropertyGetters(object obj)
		{
			if (obj == null) return new List<PropertyGetter>();

			Type type = obj.GetType();
			string typeKey = type.AssemblyQualifiedName;
			if (obj is Type)
			{
				type = (Type) obj;
				typeKey = "Static: " + type.AssemblyQualifiedName;
			}

			// ConcurrentDictionary.GetOrAdd makes the cache thread-safe (the build path runs
			// on the thread pool via the hub adapter). The factory is idempotent; a redundant
			// build under contention is harmless because only one list is stored.
			Type resolvedType = type;
			bool isStatic = obj is Type;
			return _typeHash.GetOrAdd(typeKey, _ => BuildPropertyGetters(resolvedType, isStatic));
		}

		private static List<PropertyGetter> BuildPropertyGetters(Type type, bool isStatic)
		{
			List<PropertyGetter> propertyList = new();

			IEnumerable<PropertyInfo> properties = isStatic ? GetStaticProperties(type) : GetInstanceProperties(type, null);
			foreach (PropertyInfo info in properties)
			{
				Type underlying = GetUnderlyingType(info.PropertyType);

				PropertyAttribute propAttr = GetAttribute<PropertyAttribute>(info);
				CollectionPropertyAttribute colPropAttr = propAttr as CollectionPropertyAttribute;
				ExtendedPropertyAttribute extPropAttr = propAttr as ExtendedPropertyAttribute;

				if (colPropAttr != null)
				{
					propertyList.Add(new CollectionGetter(info, colPropAttr, isStatic));
				}
				else if (extPropAttr != null)
				{
					propertyList.Add(new ExtendedPropertyGetter(info, extPropAttr, isStatic));
				}
				else if (info.PropertyType == typeof (RateCounter))
				{
					RatePropertyAttribute rateAttr = propAttr as RatePropertyAttribute;
					propertyList.Add(new RateGetter(info, rateAttr, isStatic));
				}
				else if (underlying == typeof (DateTime) || underlying == typeof(DateTimeOffset))
				{
					DatePropertyAttribute dateAttr = propAttr as DatePropertyAttribute;
					propertyList.Add(new DateGetter(info, dateAttr, isStatic));
				}
				else
				{
					propertyList.Add(new PropertyGetter(info, isStatic));
				}
			}
			return propertyList;
		}

		private static IEnumerable<PropertyInfo> GetInstanceProperties(Type type, DiagnosticClassAttribute inheritedAttr)
		{
			if (type != typeof (object))
			{
				DiagnosticClassAttribute diagAttr = GetAttribute<DiagnosticClassAttribute>(type, false);

				if (inheritedAttr == null)
				{
					for (Type baseType = type.BaseType; baseType != null && baseType != typeof(object); baseType = baseType.BaseType)
					{
						DiagnosticClassAttribute attr = GetAttribute<DiagnosticClassAttribute>(baseType, false);
						if (attr != null)
						{
							if (!attr.DeclaringTypeOnly)
							{
								inheritedAttr = attr;
							}
							break;
						}
					}
				}

				if (inheritedAttr == null || !inheritedAttr.DeclaringTypeOnly || diagAttr != null)
				{
					foreach (PropertyInfo propInfo in type.GetProperties(PublicInstancePropertyFlags | BindingFlags.DeclaredOnly))
						if (ShouldIncludeProperty(diagAttr ?? inheritedAttr, propInfo))
							yield return propInfo;
				}

				foreach (PropertyInfo propInfo in GetInstanceProperties(type.BaseType, diagAttr ?? inheritedAttr))
					yield return propInfo;
			}
		}

		private static IEnumerable<PropertyInfo> GetStaticProperties(Type type)
		{
			DiagnosticClassAttribute diagAttr = GetAttribute<DiagnosticClassAttribute>(type, false);

			return type
				.GetProperties(PublicStaticPropertyFlags)
				.Where(propInfo => ShouldIncludeProperty(diagAttr, propInfo));
		}

		private static bool ShouldIncludeProperty(DiagnosticClassAttribute diagAttr, PropertyInfo info)
		{
			if (info.PropertyType == typeof (EventSink)) return false;

			bool attributedOnly = diagAttr is { AttributedPropertiesOnly: true };
			BrowsableAttribute browseAttr = GetAttribute<BrowsableAttribute>(info);
			PropertyAttribute propAttr = GetAttribute<PropertyAttribute>(info);

			if (propAttr != null)
				return !propAttr.Ignore;

			if (browseAttr is { Browsable: false })
				return false;

			if (attributedOnly)
				return browseAttr != null;

			return true;
		}

		public static Type GetUnderlyingType(Type t)
		{
			if (t == null) throw new ArgumentNullException(nameof(t));

			if (!t.IsGenericType) return t;
			if (t.GetGenericTypeDefinition() != typeof (Nullable<>)) return t;

			return t.GetGenericArguments()[0];
		}

		private static T GetAttribute<T>(PropertyInfo info) where T : Attribute
		{
			object[] attrs = info.GetCustomAttributes(typeof (T), false);
			if (attrs.Length == 0)
				return null;

			return attrs[0] as T;
		}

		private static T GetAttribute<T>(Type info, bool inherit) where T : Attribute
		{
			object[] attrs = info.GetCustomAttributes(typeof (T), inherit);
			if (attrs.Length == 0)
				return null;

			return attrs[0] as T;
		}

		public static OperationResponse ExecuteOperation(string path, string operation, string[] arguments)
		{
			return ExecuteOperation(GetRegisteredObjects(), path, operation, arguments);
		}

		public static OperationResponse ExecuteOperation(IEnumerable<RegisteredObject> registeredObjects, string path, string operation, string[] arguments)
		{
			if (path == null)
				return OperationResponse.Error("Object path not specified");

			try
			{
				if (arguments == null)
					arguments = new string[0];

				PropIdent ident = PropIdent.Parse(path);
				object sourceObject = GetSourceObject(registeredObjects, ident);
				OperationSet opSet = GetOperationSet(sourceObject);
				if (opSet == null)
					throw new ArgumentException($"Can't find operations for {ident}");

				Operation op = opSet.Operations.FirstOrDefault(x => x.Signature == operation);
				if (op == null)
					throw new ArgumentException($"Operation '{operation}' not found");

				ParameterInfo[] parameters = op.MethodInfo.GetParameters();

				if (parameters.Length != arguments.Length)
				{
					string msg = $"Operation {operation} expected {parameters.Length} parameters, only found {arguments.Length}";
					throw new ArgumentException(msg);
				}
				object[] paramVals = new object[parameters.Length];
				for (int i = 0; i < parameters.Length; i++)
				{
					try
					{
						paramVals[i] = ConvertValue(parameters[i].ParameterType, arguments[i]);
					}
					catch (Exception ex)
					{
						string msg = $"Parameter {i + 1} ({parameters[i].Name}) can't convert '{arguments[i]}' to {TypeUtil.GetFriendlyTypeName(parameters[i].ParameterType)}";
						throw new ArgumentException(msg, ex);
					}
				}

				object result = op.MethodInfo.Invoke(sourceObject, paramVals);
				string resultString = OperationResultToString(result);
				return OperationResponse.Success(resultString);
			}
			catch (TargetInvocationException ex) when (ex.InnerException != null)
			{
				// MethodInfo.Invoke wraps anything the operation body throws in a
				// TargetInvocationException whose Message is the useless boilerplate
				// "Exception has been thrown by the target of an invocation." Surface the
				// real exception instead — for a diagnostics tool the whole point of running
				// an operation is to learn why it failed.
				Exception inner = ex.InnerException;
				return OperationResponse.Error(inner.Message, inner.ToString());
			}
			catch (Exception ex)
			{
				return OperationResponse.Error(ex.Message, ex.ToString());
			}
		}

		private static string OperationResultToString(object obj)
		{
			if (obj == null)
				return null;

			if (obj is string)
				return (string) obj;

			IEnumerable asEnumerable = obj as IEnumerable;
			if (asEnumerable == null)
				return Convert.ToString(obj);

			string[] values = asEnumerable.Cast<object>().Select(Convert.ToString).ToArray();
			if (values.Length == 0) return "<Empty>";

			return "[" + string.Join(", ", values) + "]";
		}

		/// <summary>
		/// Given an identifer which specifies the path required, this method finds the object which
		/// represents the given PropertyBag/Property Category/Property
		/// </summary>
		/// <param name="registeredObjects">The objects to search within</param>
		/// <param name="ident">Identifies the BagCat/BagName/PropCat/PropName we are searching for</param>
		/// <returns>An object which represents the Bag/PropCat/Prop, or exception if not found</returns>
		private static object GetSourceObject(IEnumerable<RegisteredObject> registeredObjects, PropIdent ident)
		{
			PropertyBag bag = GetRegisteredObject(registeredObjects, ident);

			if (string.IsNullOrEmpty(ident.PropCategory) && string.IsNullOrEmpty(ident.PropName))
			{
				if (bag.SourceObject == null)
				{
					string msg = $"Can't invoke operation. Property bag {ident.BagCategory}|{ident.BagName} doesn't have a value.";
					throw new ArgumentException(msg);
				}
				return bag.SourceObject;
			}

			Category cat = bag.Categories.FindByName(ident.PropCategory);
			if (cat == null)
			{
				string msg = $"Can't find source category {ident.BagCategory}|{ident.BagName}|{ident.PropCategory}";

				throw new ArgumentException(msg);
			}

			if (string.IsNullOrEmpty(ident.PropName))
			{
				if (cat.ValueObject == null)
				{
					string msg = $"Can't invoke operation. Category {ident.BagCategory}|{ident.BagName}|{ident.PropCategory} doesn't have a value.";
					throw new ArgumentException(msg);
				}
				return cat.ValueObject;
			}

			Property prop = cat.Properties.FindByName(ident.PropName);
			if (prop == null)
			{
				string msg = $"Can't invoke operation. Property {ident.BagCategory}|{ident.BagName}|{ident.PropCategory} not found.";
				throw new ArgumentException(msg);
			}

			if (prop.ValueObject == null)
			{
				string msg = $"Can't invoke operation. Property {ident.BagCategory}|{ident.BagName}|{ident.PropCategory} doesn't have a value.";
				throw new ArgumentException(msg);
			}

			return prop.ValueObject;
		}

		#region SetProperty

		public static OperationResponse SetProperty(string path, string value)
		{
			return SetProperty(GetRegisteredObjects(), path, value);
		}

		public static OperationResponse SetProperty(IEnumerable<RegisteredObject> registeredObjects, string path, string value)
		{
			try
			{
				if (path == null)
					return OperationResponse.Error("Property path not specified");

				PropIdent ident = PropIdent.Parse(path);
				RegisteredObject regObj = registeredObjects.FindByCategoryAndName(ident.BagCategory, ident.BagName);
				if (regObj == null)
					return OperationResponse.Error($"Can't find PropertyBag {ident.BagCategory}.{ident.BagName}");

				object obj = regObj.Object;
				if (obj == null)
					return OperationResponse.Error($"PropertyBag {ident.BagCategory}.{ident.BagName} was garbage collected just before I could set the property.  How bizarre!");

				List<PropertyGetter> valueGetters = GetPropertyGetters(obj);
				PropertyGetter getter = valueGetters.FirstOrDefault(g => 
					_ignoreCase.Equals(g.Name, ident.PropName) && 
					_ignoreCase.Equals(g.Category ?? "", ident.PropCategory ?? ""));

				if (getter == null)
					return OperationResponse.Error($"Can't find property [{ident.PropCategory}].[{ident.PropName}]");

				if (getter.PropInfo == null)
					return OperationResponse.Error($"Property [{ident.PropCategory}].[{ident.PropName}] doesn't have a source PropertyInfo!");

				if (!getter.CanSet)
					return OperationResponse.Error($"You are not allowed to set [{ident.PropCategory}].[{ident.PropName}], AllowSet is not enabled!");

				bool isType = obj is Type;
				if (!isType && !getter.PropInfo.DeclaringType.IsInstanceOfType(obj))
					return OperationResponse.Error($"'{ident.PropCategory}'.'{ident.PropName}' property {getter.PropInfo.Name} expects type {getter.PropInfo.DeclaringType.Name}, got {obj.GetType().Name}");

				object newValue = ConvertValue(getter.PropInfo.PropertyType, value);
				if (isType)
					getter.PropInfo.SetValue(null, newValue, null);
				else
					getter.PropInfo.SetValue(obj, newValue, null);

				return OperationResponse.Success();
			}
			catch (TargetInvocationException ex) when (ex.InnerException != null)
			{
				Exception inner = ex.InnerException;
				return OperationResponse.Error(inner.Message, inner.ToString());
			}
			catch (Exception ex)
			{
				return OperationResponse.Error(ex.Message, ex.ToString());
			}
		}

		private static PropertyBag GetRegisteredObject(IEnumerable<RegisteredObject> registeredObjects, PropIdent ident)
		{
			RegisteredObject regObj = registeredObjects.FindByCategoryAndName(ident.BagCategory, ident.BagName);
			if (regObj == null)
				throw new ArgumentException($"Can't find PropertyBag {ident.BagCategory}.{ident.BagName}");

			object obj = regObj.Object;
			if (obj == null)
			{
				string msg = $"PropertyBag {ident.BagCategory}.{ident.BagName} was garbage collected just before I could set the property.  How bizarre!";
				throw new ArgumentException(msg);
			}

			return ObjectToPropertyBag(obj, ident.BagName, ident.BagCategory);
		}

		#endregion

		private class PropIdent
		{
			public string BagCategory { get; private set; }
			public string BagName { get; private set; }
			public string PropCategory { get; private set; }
			public string PropName { get; private set; }

			public static PropIdent Parse(string path)
			{
				if (string.IsNullOrWhiteSpace(path))
					throw new ArgumentException("Path cannot be empty.");

				string[] elements = path.Split('|');
				if (elements.Length < 2 || string.IsNullOrEmpty(elements[0]) || string.IsNullOrEmpty(elements[1]))
					throw new ArgumentException($"Invalid property/operation path: '{path}'. Path must contain at least a category and name, separated by '|'.");

				PropIdent ident = new();
				ident.BagCategory = NullIfEmpty(elements.ElementAtOrDefault(0));
				ident.BagName = NullIfEmpty(elements.ElementAtOrDefault(1));
				ident.PropCategory = NullIfEmpty(elements.ElementAtOrDefault(2));
				ident.PropName = NullIfEmpty(elements.ElementAtOrDefault(3));
				return ident;
			}

			public override string ToString()
			{
				if (PropName != null)
					return $"{BagCategory}|{BagName}|{PropCategory}|{PropName}";

				if (PropCategory != null)
					return $"{BagCategory}|{BagName}|{PropCategory}";

				return $"{BagCategory}|{BagName}";
			}

			private static string NullIfEmpty(string s)
			{
				return string.IsNullOrEmpty(s) ? null : s;
			}
		}

		private static object ConvertValue(Type type, string value)
		{
			if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof (Nullable<>))
			{
				if (string.IsNullOrEmpty(value))
					return null;

				type = type.GetGenericArguments()[0];
			}

			if (type.IsEnum)
				return Enum.Parse(type, value, true);

			try
			{
				return Convert.ChangeType(value, type);
			}
			catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
			{
				object parsed;
				if (TryParseValue(type, value, out parsed))
					return parsed;

				throw;
			}
		}

		private static bool TryParseValue(Type type, string value, out object parsed)
		{
			parsed = null;

			MethodInfo method = type.GetMethod("Parse", PublicStaticMethods, null, new[] {typeof (string)}, null);

			if (method == null)
				return false;

			try
			{
				parsed = method.Invoke(null, new object[] {value});
				return true;
			}
			catch (TargetInvocationException ex)
			{
				if (ex.InnerException != null)
				{
					System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
				}
				throw;
			}
		}
	}
}