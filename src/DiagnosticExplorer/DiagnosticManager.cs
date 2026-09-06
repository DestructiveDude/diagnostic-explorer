using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using DiagnosticExplorer.Logging;
using DiagnosticExplorer.Util;

namespace DiagnosticExplorer;

public static class DiagnosticManager
{
    private static readonly StringComparer _ignoreCase = StringComparer.CurrentCultureIgnoreCase;
    private static List<RegisteredObject> RegisteredObjects { get; set; }

    private static readonly ConcurrentDictionary<string, List<PropertyGetter>> _typeHash = new();

    private static readonly ConcurrentDictionary<Type, Lazy<OperationSet>> _operationLookup = new();
    private static readonly ConcurrentDictionary<Type, Lazy<OperationSet>> _staticOperationLookup = new();
    private static int _operationSetId;
    public static bool Enabled { get; set; } = true;

    private static DiagnosticConfigurationSnapshot _configuration = DiagnosticConfigurationSnapshot.Empty;

    /// <summary>
    ///     Bumped by every <see cref="UseConfiguration" /> and stamped into the getter cache key, so
    ///     entries built under a superseded configuration can never be read back.
    /// </summary>
    private static int _configurationVersion;

    /// <summary>The configuration currently in force. Replaced wholesale by <see cref="UseConfiguration" />.</summary>
    public static DiagnosticConfiguration CurrentConfiguration { get; private set; } = new();

    /// <summary>Builds a configuration fluently and applies it.</summary>
    public static DiagnosticConfiguration Configure(Action<IDiagConfigurator> configure)
    {
        if (configure == null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        DiagnosticConfiguration configuration = new();
        configure(configuration);
        UseConfiguration(configuration);
        return configuration;
    }

    /// <summary>
    ///     Applies a configuration, replacing whatever was in force.
    /// </summary>
    /// <remarks>
    ///     Clearing the getter cache is not optional: getters are built once per type and bake the
    ///     configuration in, so a reconfiguration that left the cache alone would apply to types
    ///     nothing had touched yet and silently not to the rest.
    /// </remarks>
    public static void UseConfiguration(DiagnosticConfiguration configuration)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        CurrentConfiguration = configuration;
        _configuration = configuration.CreateSnapshot();

        // Only when the configuration actually says so. Enabled is a public, directly-settable
        // toggle, so assigning it unconditionally would silently switch diagnostics back on for a
        // host that had turned them off and then reconfigured something unrelated.
        if (configuration.RuntimeOptions.EnabledIsSet)
        {
            Enabled = configuration.RuntimeOptions.Enabled;
        }

        LogEventStore.Configure(
            configuration.RuntimeOptions.LogEventRetention,
            configuration.RuntimeOptions.Routing.CreateSnapshot()
        );

        // Upstream also pushes RuntimeOptions.EventRetention into EventSinkRepo here. That needs
        // the EventSink retention rework, which is still unported, so event-sink retention keeps
        // its existing behaviour and only the log stream is retuned.
        Interlocked.Increment(ref _configurationVersion);
        _typeHash.Clear();
    }

    /// <summary>
    ///     How many items a drill-down materialises before truncating.
    /// </summary>
    /// <remarks>
    ///     Upstream reads this from its configuration snapshot. That surface arrives with the fluent
    ///     configuration port; until then this is upstream's own default, so behaviour matches and
    ///     only the ability to retune it is missing.
    /// </remarks>
    internal static int DrillDownMaxItems => 100;

    /// <summary>
    ///     Whether a value is worth drilling into, or is a leaf best rendered in place. Strings and
    ///     scalars are leaves; so are UI elements, whose property graphs are enormous and circular.
    /// </summary>
    internal static bool IsDrillDownValue(object value)
    {
        if (value == null || value is string)
        {
            return false;
        }

        Type type = Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();
        if (
            type.IsPrimitive
            || type.IsEnum
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan)
            || type == typeof(Guid)
        )
        {
            return false;
        }

        return !IsUserInterfaceElement(type);
    }

    /// <summary>
    ///     Matched by full name rather than by reference, so the core library keeps no dependency on
    ///     WinForms or WPF and this stays correct on every target framework.
    /// </summary>
    private static bool IsUserInterfaceElement(Type type)
    {
        for (Type baseType = type; baseType != null; baseType = baseType.BaseType)
        {
            if (
                baseType.FullName == "System.Windows.Forms.Control"
                || baseType.FullName == "System.Windows.FrameworkElement"
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     The process-wide log stream. Logging-framework adapters publish here when constructed
    ///     without an explicit store, so a host that configures none still gets a usable stream.
    /// </summary>
    public static LogEventStore LogEventStore { get; } = new();

    static DiagnosticManager()
    {
        RegisteredObjects = [];
    }

    internal static void Clear()
    {
        _operationLookup.Clear();
        _staticOperationLookup.Clear();
        _typeHash.Clear();
        lock (RegisteredObjects)
        {
            RegisteredObjects.Clear();
        }
    }

    public static void Register(object o, string bagName, string bagCategory)
    {
        if (!Enabled)
        {
            return;
        }

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

    [SuppressMessage(
        "Reliability",
        "S1994:For loop increment clauses should modify the loop counters",
        Justification = "The unbounded suffix search always returns at the first available name."
    )]
    private static string MakeNameUnique(RegisteredObject obj, string name, string category)
    {
        if (name == null)
        {
            // ReSharper disable once ExpressionIsAlwaysNull -- legacy callers may supply null.
            return name;
        }

        var takenNames = new HashSet<string>(_ignoreCase);
        foreach (
            var ro in RegisteredObjects.Where(ro =>
                !ReferenceEquals(ro, obj) && _ignoreCase.Equals(category, ro.BagCategory)
            )
        )
        {
            takenNames.Add(ro.BagName);
        }

        if (!takenNames.Contains(name))
        {
            return name;
        }

        for (int i = 2; ; i++)
        {
            string extension = $" {i}";
            string newName = $"{name}{extension}";
            if (!takenNames.Contains(newName))
            {
                return newName;
            }
        }
    }

    public static void Unregister(object obj)
    {
        lock (RegisteredObjects)
        {
            RegisteredObject existing = RegisteredObjects.Find(ro => ReferenceEquals(ro.Object, obj));

            if (existing != null)
            {
                RegisteredObjects.Remove(existing);
            }
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
                registeredObjects.Select(x => ObjectToPropertyBag(x.Object, x.BagName, x.BagCategory))
            );

            HashSet<OperationSet> operationSets = [];

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
            return new DiagnosticResponse { ExceptionMessage = ex.Message, ExceptionDetail = ex.ToString() };
        }
    }

    private static OperationSet GetOperationSet(object sourceObject)
    {
        if (sourceObject == null)
        {
            return null;
        }

        if (sourceObject is Type type)
        {
            Lazy<OperationSet> lazy = _staticOperationLookup.GetOrAdd(
                type,
                t => new Lazy<OperationSet>(() => BuildStaticOperationSet(t))
            );
            return lazy.Value;
        }

        Type propType = sourceObject.GetType();
        Lazy<OperationSet> lazyInstance = _operationLookup.GetOrAdd(
            propType,
            t => new Lazy<OperationSet>(() => BuildOperationSet(t))
        );
        return lazyInstance.Value;
    }

    private static OperationSet BuildOperationSet(Type propType)
    {
        OperationSet operationSet = CreateOperationSet(propType);
        operationSet?.Id = Interlocked.Increment(ref _operationSetId).ToString();

        return operationSet;
    }

    private static OperationSet BuildStaticOperationSet(Type propType)
    {
        OperationSet operationSet = CreateStaticOperationSet(propType);
        operationSet?.Id = Interlocked.Increment(ref _operationSetId).ToString();

        return operationSet;
    }

    private static OperationSet CreateOperationSet(Type propType)
    {
        Guard.NotNull(propType, nameof(propType));

        if (propType.FullName == null)
        {
            return null;
        }

        if (propType.FullName.StartsWith("System."))
        {
            return null;
        }

        OperationSet operationSet = new();

        foreach (
            MethodInfo method in propType
                .GetMethods(PublicMethods)
                .Where(IsMethodValidOperationTarget)
                .OrderBy(x => x.Name)
        )
        {
            operationSet.Operations.Add(new Operation(method));
        }

        return operationSet.Operations.Count == 0 ? null : operationSet;
    }

    private static OperationSet CreateStaticOperationSet(Type propType)
    {
        Guard.NotNull(propType, nameof(propType));

        if (propType.FullName == null)
        {
            return null;
        }

        if (propType.FullName.StartsWith("System."))
        {
            return null;
        }

        OperationSet operationSet = new();

        foreach (
            MethodInfo method in propType
                .GetMethods(PublicStaticMethods)
                .Where(IsMethodValidOperationTarget)
                .OrderBy(x => x.Name)
        )
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
        if (method.IsSpecialName)
        {
            return false;
        }

        if (method.IsGenericMethod)
        {
            return false;
        }

        if (method.GetParameters().Any(x => x.IsOut))
        {
            return false;
        }

        if (method.GetParameters().Any(x => x.ParameterType.IsByRef))
        {
            return false;
        }

        return AttributeUtil.GetAttribute<DiagnosticMethodAttribute>(method) != null;
    }

    public static RegisteredObject[] GetRegisteredObjects()
    {
        List<RegisteredObject> list = [];

        lock (RegisteredObjects)
        {
            for (int i = RegisteredObjects.Count - 1; i >= 0; i--)
            {
                RegisteredObject obj = RegisteredObjects[i];
                if (obj.Object == null)
                {
                    RegisteredObjects.RemoveAt(i);
                }
                else
                {
                    list.Add(obj);
                }
            }
        }
        return [.. list];
    }

    [ThreadStatic]
    private static HashSet<object> _visitedObjects;

    internal static HashSet<object> VisitedObjects =>
        _visitedObjects ??= new HashSet<object>(new ReferenceEqualityComparer());

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        bool IEqualityComparer<object>.Equals(object x, object y) => ReferenceEquals(x, y);

        int IEqualityComparer<object>.GetHashCode(object obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    public static PropertyBag ObjectToPropertyBag(object obj, string bagName, string bagCategory)
    {
        PropertyBag bag = new()
        {
            Name = bagName,
            Category = bagCategory,
            SourceObject = obj,
        };

        var visited = VisitedObjects;
        visited.Clear();
        try
        {
            if (obj != null)
            {
                visited.Add(obj);
            }

            List<PropertyGetter> valueGetters = GetPropertyGetters(obj);

            foreach (PropertyGetter getter in valueGetters)
            {
                // ReSharper disable once AssignNullToNotNullAttribute -- null means no category prefix.
                getter.GetProperties(obj, bag, null);
            }
        }
        finally
        {
            visited.Clear();
        }

        return bag;
    }

    public const BindingFlags PublicInstancePropertyFlags =
        BindingFlags.Public | BindingFlags.GetProperty | BindingFlags.Instance;
    private const BindingFlags PublicStaticPropertyFlags =
        BindingFlags.Public | BindingFlags.GetProperty | BindingFlags.Static;
    private const BindingFlags PublicMethods = BindingFlags.Public | BindingFlags.InvokeMethod | BindingFlags.Instance;
    private const BindingFlags PublicStaticMethods =
        BindingFlags.Static | BindingFlags.InvokeMethod | BindingFlags.Public;

    internal static List<PropertyGetter> GetPropertyGetters(object obj)
    {
        if (obj == null)
        {
            return [];
        }

        Type type = obj.GetType();
        string typeKey = type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
        if (obj is Type t)
        {
            type = t;
            typeKey = "Static: " + (type.AssemblyQualifiedName ?? type.FullName ?? type.Name);
        }

        // ConcurrentDictionary.GetOrAdd makes the cache thread-safe (the build path runs
        // on the thread pool via the hub adapter). The factory is idempotent; a redundant
        // build under contention is harmless because only one list is stored.
        //
        // The key carries the configuration version because clearing the cache is not enough on
        // its own: a thread already inside GetOrAdd with the previous snapshot can insert its
        // getters just after UseConfiguration clears, leaving that type stale until the next
        // reconfiguration. Stamping the version means such a write lands under the old key and is
        // simply never read again.
        int version = Volatile.Read(ref _configurationVersion);
        string versionedKey = version + ":" + typeKey;
        Type resolvedType = type;
        bool isStatic = obj is Type;
        return _typeHash.GetOrAdd(versionedKey, _ => BuildPropertyGetters(resolvedType, isStatic));
    }

    /// <summary>
    ///     Builds the getters for one property, choosing the presentation strategy from the
    ///     attribute, the configuration, or the property's own type.
    /// </summary>
    /// <remarks>
    ///     The single seam between the configuration model and the getter pipeline. Both routes —
    ///     an attribute on the property, or a <see cref="PropertyConfiguration" /> built fluently —
    ///     arrive here and converge on the same getters, which is what stops the two ways of
    ///     configuring a property drifting apart.
    /// </remarks>
    internal static void AddPropertyGetters(
        ICollection<PropertyGetter> getters,
        PropertyInfo info,
        DiagnosticPropertyAttribute metadata,
        PropertyConfiguration configuration,
        bool isStatic,
        bool applyAttributes,
        string defaultFormat,
        bool useDefaultPropertyPresentation = false
    )
    {
        PropertyStrategy strategy = GetPropertyStrategy(info, metadata, configuration, useDefaultPropertyPresentation);
        switch (strategy)
        {
            case PropertyStrategy.Collection:
                AddCollectionGetters(getters, info, metadata, configuration, isStatic, applyAttributes, defaultFormat);
                break;
            case PropertyStrategy.Extended:
                getters.Add(
                    new ExtendedPropertyGetter(
                        info,
                        new ExtendedPropertyAttribute(),
                        metadata,
                        configuration,
                        isStatic,
                        applyAttributes,
                        defaultFormat
                    )
                );
                break;
            case PropertyStrategy.Rate:
                getters.Add(
                    new RateGetter(
                        info,
                        CreateRateOptions(metadata as RatePropertyAttribute, configuration),
                        metadata,
                        configuration,
                        isStatic,
                        applyAttributes,
                        defaultFormat
                    )
                );
                break;
            case PropertyStrategy.Date:
                getters.Add(
                    new DateGetter(
                        info,
                        CreateDateOptions(metadata as DatePropertyAttribute, configuration),
                        metadata,
                        configuration,
                        isStatic,
                        applyAttributes,
                        defaultFormat
                    )
                );
                break;
            default:
                getters.Add(
                    new PropertyGetter(
                        info,
                        metadata,
                        configuration,
                        isStatic,
                        applyAttributes,
                        defaultFormat,
                        useDefaultPropertyPresentation
                            && IsDefaultObjectType(info.PropertyType)
                            && !HasUsefulToString(info.PropertyType)
                    )
                );
                break;
        }
    }

    private static PropertyStrategy GetPropertyStrategy(
        PropertyInfo info,
        DiagnosticPropertyAttribute attribute,
        PropertyConfiguration configuration,
        bool useDefaultPropertyPresentation
    )
    {
        // An explicit configuration wins over everything; then an explicit attribute; then the
        // property's own type. Order matters: a RateCounter-typed property is a rate whether or not
        // it carries the attribute, but a configuration saying otherwise still overrides that.
        if (configuration?.Strategy != null)
        {
            return configuration.Strategy.Value;
        }

        if (attribute is CollectionPropertyAttribute)
        {
            return PropertyStrategy.Collection;
        }

        if (attribute is ExtendedPropertyAttribute)
        {
            return PropertyStrategy.Extended;
        }

        Type propertyType = info?.PropertyType ?? configuration?.ValueType;
        if (attribute is RatePropertyAttribute || propertyType == typeof(RateCounter))
        {
            return PropertyStrategy.Rate;
        }

        if (propertyType == null)
        {
            // A configured property can have neither a PropertyInfo nor a declared value type.
            // GetUnderlyingType guards against null by throwing, so fall back rather than fail.
            return PropertyStrategy.Default;
        }

        Type underlying = GetUnderlyingType(propertyType);
        if (
            attribute is DatePropertyAttribute
            || underlying == typeof(DateTime)
            || underlying == typeof(DateTimeOffset)
        )
        {
            return PropertyStrategy.Date;
        }

        if (configuration?.UsesPropertyDefaults == true && UsesDefaultCollectionPresentation(underlying, configuration))
        {
            return PropertyStrategy.Collection;
        }

        return useDefaultPropertyPresentation && IsDefaultCollectionType(underlying)
            ? PropertyStrategy.Collection
            : PropertyStrategy.Default;
    }

    private static bool UsesDefaultCollectionPresentation(Type type, PropertyConfiguration configuration)
    {
        return configuration.ValueFormatter == null
            && !configuration.FormatString.IsSet
            && IsConfiguredCollectionType(type);
    }

    private static bool IsConfiguredCollectionType(Type type)
    {
        Type underlyingType = GetUnderlyingType(type);
        if (underlyingType == typeof(string))
        {
            return false;
        }

        if (underlyingType.IsArray)
        {
            return true;
        }

        return ImplementsGenericInterface(underlyingType, typeof(ICollection<>))
            || ImplementsGenericInterface(underlyingType, typeof(IList<>))
            || ImplementsGenericInterface(underlyingType, typeof(IReadOnlyCollection<>))
            || ImplementsGenericInterface(underlyingType, typeof(IReadOnlyList<>))
            || ImplementsGenericInterface(underlyingType, typeof(ISet<>));
    }

    private static bool ImplementsGenericInterface(Type type, Type genericInterface)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == genericInterface
            || type.GetInterfaces()
                .Any(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == genericInterface);
    }

    private static void AddCollectionGetters(
        ICollection<PropertyGetter> getters,
        PropertyInfo info,
        DiagnosticPropertyAttribute metadata,
        PropertyConfiguration configuration,
        bool isStatic,
        bool applyAttributes,
        string defaultFormat
    )
    {
        CollectionOptions source = (metadata as CollectionPropertyAttribute)?.CreateOptions();
        IReadOnlyList<CollectionOutputConfiguration> outputs = configuration?.CollectionOutputs;
        if (outputs == null || outputs.Count == 0)
        {
            CollectionOptions options = CloneCollectionOptions(source);
            ApplyCollectionConfiguration(options, configuration);
            getters.Add(
                new CollectionGetter(info, options, metadata, configuration, isStatic, applyAttributes, defaultFormat)
            );
            return;
        }

        // One collection property can be projected several ways at once - a count alongside a
        // listing, say - so each configured output becomes its own getter over the same property.
        foreach (CollectionOutputConfiguration output in outputs)
        {
            CollectionOptions options = CloneCollectionOptions(source);
            options.Mode = output.Mode;
            options.NameProperty = output.NameProperty ?? options.NameProperty;
            options.NameFormatter = output.NameFormatter ?? options.NameFormatter;
            options.IndexedNameFormatter = output.IndexedNameFormatter ?? options.IndexedNameFormatter;
            options.ValueProperty = output.ValueProperty ?? options.ValueProperty;
            options.ValueFormatter = output.ValueFormatter ?? options.ValueFormatter;
            options.ItemIsJson = output.ItemIsJson;
            options.DescriptionProperty = output.DescriptionProperty ?? options.DescriptionProperty;
            options.DescriptionFormatter = output.DescriptionFormatter ?? options.DescriptionFormatter;
            options.CategoryProperty = output.CategoryProperty ?? options.CategoryProperty;
            options.CategoryFormatter = output.CategoryFormatter ?? options.CategoryFormatter;
            options.Separator = output.Separator ?? options.Separator;
            options.InitiallyExpanded = output.InitiallyExpanded;
            options.PrimaryPropertiesOnly = output.PrimaryPropertiesOnly;
            options.ItemStatuses = output.ItemStatuses;
            options.ItemStatusIconSize = output.ItemStatusIconSize;
            options.ItemWidth = output.ItemWidth;
            ApplyCollectionConfiguration(options, configuration);

            PropertyConfiguration outputConfiguration = configuration.Clone();
            ApplyCollectionOutputConfiguration(outputConfiguration, output);
            string outputName = output.Name;
            if (outputName == null && outputs.Count > 1 && output.Mode == CollectionMode.Count)
            {
                // Several outputs over one property would otherwise collide on the same name.
                string baseName = configuration.Name.IsSet ? configuration.Name.Value : metadata?.Name ?? info?.Name;
                outputName = baseName + " count";
            }

            if (outputName != null)
            {
                // Set the name on the configuration that already carries this output's overrides.
                // Upstream re-clones from the original here, which silently discards everything
                // ApplyCollectionOutputConfiguration just applied - drill-down, hover, truncation -
                // for every named output.
                outputConfiguration.Name = new ConfiguredValue<string>(outputName);
            }

            getters.Add(
                new CollectionGetter(
                    info,
                    options,
                    metadata,
                    outputConfiguration,
                    isStatic,
                    applyAttributes,
                    defaultFormat
                )
            );
        }
    }

    private static CollectionOptions CloneCollectionOptions(CollectionOptions source)
    {
        if (source == null)
        {
            return new CollectionOptions(CollectionMode.Count);
        }

        return new CollectionOptions(source.Mode)
        {
            NameProperty = source.NameProperty,
            NameFormatter = source.NameFormatter,
            IndexedNameFormatter = source.IndexedNameFormatter,
            ValueProperty = source.ValueProperty,
            ValueFormatter = source.ValueFormatter,
            ItemIsJson = source.ItemIsJson,
            DescriptionProperty = source.DescriptionProperty,
            DescriptionFormatter = source.DescriptionFormatter,
            CategoryProperty = source.CategoryProperty,
            CategoryFormatter = source.CategoryFormatter,
            Separator = source.Separator,
            MaxItems = source.MaxItems,
            InitiallyExpanded = source.InitiallyExpanded,
            PrimaryPropertiesOnly = source.PrimaryPropertiesOnly,
            ItemStatuses = source.ItemStatuses,
            ItemStatusIconSize = source.ItemStatusIconSize,
            ItemWidth = source.ItemWidth,
        };
    }

    private static void ApplyCollectionConfiguration(CollectionOptions options, PropertyConfiguration configuration)
    {
        if (configuration != null && configuration.MaxItems.IsSet)
        {
            options.MaxItems = configuration.MaxItems.Value;
        }
    }

    private static void ApplyCollectionOutputConfiguration(
        PropertyConfiguration configuration,
        CollectionOutputConfiguration output
    )
    {
        configuration.NoTruncate = output.NoTruncate.Or(configuration.NoTruncate);
        configuration.DrillDown = output.DrillDown.Or(configuration.DrillDown);
        configuration.DrillDownMaxItems = output.DrillDownMaxItems.Or(configuration.DrillDownMaxItems);
        configuration.DrillDownIconOnly = output.DrillDownIconOnly.Or(configuration.DrillDownIconOnly);
        configuration.DrillDownText = output.DrillDownText.Or(configuration.DrillDownText);
        configuration.DrillDownTextFormatter = output.DrillDownTextFormatter ?? configuration.DrillDownTextFormatter;
        configuration.JsonHover = output.JsonHover.Or(configuration.JsonHover);
        configuration.ExpandedHover = output.ExpandedHover.Or(configuration.ExpandedHover);
    }

    private static RatePropertyAttribute CreateRateOptions(
        RatePropertyAttribute source,
        PropertyConfiguration configuration
    )
    {
        if (source == null && configuration?.Strategy != PropertyStrategy.Rate)
        {
            return null;
        }

        RatePropertyAttribute options =
            source == null
                ? new RatePropertyAttribute()
                : new RatePropertyAttribute { ExposeRate = source.ExposeRate, ExposeTotal = source.ExposeTotal };

        if (configuration != null)
        {
            if (configuration.ExposeRate.IsSet)
            {
                options.ExposeRate = configuration.ExposeRate.Value;
            }

            if (configuration.ExposeTotal.IsSet)
            {
                options.ExposeTotal = configuration.ExposeTotal.Value;
            }
        }

        return options;
    }

    /// <summary>
    ///     Builds date options, or null when neither an attribute nor the configuration says
    ///     anything about dates.
    /// </summary>
    /// <remarks>
    ///     Returning null matters. DateGetter defaults ExposeDate to true and overrides it only when
    ///     handed a non-null attribute, while our DatePropertyAttribute.ExposeDate defaults to false
    ///     - upstream's carries a constructor setting it true, ours does not. Handing back a default
    ///     attribute for an unattributed DateTime property would therefore switch the date off for
    ///     every one of them, silently. Same guard shape as CreateRateOptions.
    /// </remarks>
    private static DatePropertyAttribute CreateDateOptions(
        DatePropertyAttribute source,
        PropertyConfiguration configuration
    )
    {
        bool configuresDates =
            configuration != null
            && (
                configuration.ExposeDate.IsSet
                || configuration.ExposeElapsed.IsSet
                || configuration.ExposeTimeUntil.IsSet
            );
        if (source == null && !configuresDates)
        {
            return null;
        }

        DatePropertyAttribute options =
            source == null
                ? new DatePropertyAttribute { ExposeDate = true }
                : new DatePropertyAttribute
                {
                    ExposeDate = source.ExposeDate,
                    ExposeElapsed = source.ExposeElapsed,
                    ExposeTimeUntil = source.ExposeTimeUntil,
                    IsUTC = source.IsUTC,
                };

        if (configuration != null)
        {
            if (configuration.ExposeDate.IsSet)
            {
                options.ExposeDate = configuration.ExposeDate.Value;
            }

            if (configuration.ExposeElapsed.IsSet)
            {
                options.ExposeElapsed = configuration.ExposeElapsed.Value;
            }

            if (configuration.ExposeTimeUntil.IsSet)
            {
                options.ExposeTimeUntil = configuration.ExposeTimeUntil.Value;
            }
        }

        return options;
    }

    private static bool IsDefaultObjectType(Type type)
    {
        Type underlyingType = GetUnderlyingType(type);
        return !IsDefaultDiagnosticPropertyType(underlyingType)
            && !IsDefaultCollectionType(underlyingType)
            && !IsExcludedDefaultDiagnosticPropertyType(underlyingType);
    }

    private static bool IsDefaultDiagnosticPropertyType(Type type)
    {
        Type underlyingType = GetUnderlyingType(type);
        return underlyingType == typeof(string)
            || underlyingType.IsPrimitive
            || underlyingType.IsEnum
            || underlyingType == typeof(decimal)
            || underlyingType == typeof(DateTime)
            || underlyingType == typeof(DateTimeOffset)
            || underlyingType == typeof(TimeSpan)
            || underlyingType == typeof(Guid);
    }

    /// <summary>
    ///     Tasks are excluded from default object presentation: walking one's properties forces
    ///     evaluation of Result and can deadlock or surface an exception that was never observed.
    /// </summary>
    private static bool IsExcludedDefaultDiagnosticPropertyType(Type type)
    {
        Type underlyingType = GetUnderlyingType(type);
        return underlyingType.Namespace?.StartsWith("System.Threading.Tasks", StringComparison.Ordinal) == true;
    }

    private static bool IsDefaultCollectionType(Type type)
    {
        Type underlyingType = GetUnderlyingType(type);
        return underlyingType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(underlyingType);
    }

    /// <summary>
    ///     Whether a type overrides ToString itself. A type that does not would render as its type
    ///     name, which is worth nothing, so the default presentation expands it instead.
    /// </summary>
    private static bool HasUsefulToString(Type type)
    {
        Type underlyingType = GetUnderlyingType(type);
        MethodInfo toString = underlyingType.GetMethod(nameof(ToString), Type.EmptyTypes);
        return toString != null
            && toString.DeclaringType != typeof(object)
            && toString.DeclaringType != typeof(ValueType);
    }

    /// <summary>
    ///     Builds the getters for one type, from its attributes and from whatever the configuration
    ///     in force says about it.
    /// </summary>
    /// <remarks>
    ///     The strategy dispatch that used to live here now sits in <see cref="AddPropertyGetters" />,
    ///     so an attribute-configured and a fluently-configured property go through one path. This
    ///     is where the configuration stops being inert.
    /// </remarks>
    private static List<PropertyGetter> BuildPropertyGetters(Type type, bool isStatic)
    {
        List<PropertyGetter> propertyList = [];
        DiagnosticConfigurationSnapshot configuration = _configuration;
        bool applyAttributes = configuration.ApplyAttributes;
        TypeConfiguration typeConfiguration = isStatic ? null : configuration.GetEffectiveTypeConfiguration(type);

        IEnumerable<PropertyInfo> properties = isStatic
            ? GetStaticProperties(type)
            : GetInstanceProperties(type, null, typeConfiguration);
        foreach (PropertyInfo info in properties)
        {
            DiagnosticPropertyAttribute propAttr = applyAttributes
                ? GetAttribute<DiagnosticPropertyAttribute>(info)
                : null;
            PropertyConfiguration propertyConfiguration = typeConfiguration?.Find(info);
            string defaultFormat = configuration.GetDefaultFormat(info.PropertyType);
            AddPropertyGetters(
                propertyList,
                info,
                propAttr,
                propertyConfiguration,
                isStatic,
                applyAttributes,
                defaultFormat
            );
        }

        if (typeConfiguration != null)
        {
            // Properties that exist only in configuration: a delegate over the object, or a fully
            // custom projection. Neither has a PropertyInfo, so neither is reachable above.
            foreach (PropertyConfiguration delegateProperty in typeConfiguration.DelegateProperties)
            {
                string defaultFormat = configuration.GetDefaultFormat(delegateProperty.ValueType);
                AddPropertyGetters(propertyList, null, null, delegateProperty, false, false, defaultFormat);
            }

            foreach (CustomPropertyConfiguration customProperty in typeConfiguration.CustomProperties)
            {
                propertyList.Add(new CustomPropertyGetter(customProperty));
            }
        }

        return propertyList;
    }

    private static IEnumerable<PropertyInfo> GetInstanceProperties(
        Type type,
        DiagnosticClassAttribute inheritedAttr,
        TypeConfiguration typeConfiguration
    )
    {
        return GetInstanceProperties(
            type,
            inheritedAttr,
            typeConfiguration,
            new HashSet<string>(StringComparer.Ordinal)
        );
    }

    [SuppressMessage(
        "Maintainability",
        "S3776:Cognitive Complexity of methods should not be too high",
        Justification = "This recursive reflection walk preserves inherited diagnostic metadata."
    )]
    [SuppressMessage(
        "CodeQuality",
        "S3267:Loops should be simplified with LINQ expressions",
        Justification = "The loop yields recursively discovered properties and cannot be flattened safely."
    )]
    private static IEnumerable<PropertyInfo> GetInstanceProperties(
        Type type,
        DiagnosticClassAttribute inheritedAttr,
        TypeConfiguration typeConfiguration,
        HashSet<string> yieldedNames
    )
    {
        if (type != typeof(object) && type != null)
        {
            DiagnosticClassAttribute diagAttr = GetAttribute<DiagnosticClassAttribute>(type, false);

            if (inheritedAttr == null)
            {
                for (
                    Type baseType = type.BaseType;
                    baseType != null && baseType != typeof(object);
                    baseType = baseType.BaseType
                )
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
                foreach (
                    PropertyInfo propInfo in type.GetProperties(PublicInstancePropertyFlags | BindingFlags.DeclaredOnly)
                        .Where(p => ShouldIncludeProperty(diagAttr ?? inheritedAttr, p, typeConfiguration))
                )
                {
                    if (yieldedNames.Add(propInfo.Name))
                    {
                        yield return propInfo;
                    }
                }
            }

            foreach (
                PropertyInfo propInfo in GetInstanceProperties(
                    type.BaseType,
                    diagAttr ?? inheritedAttr,
                    typeConfiguration,
                    yieldedNames
                )
            )
            {
                yield return propInfo;
            }
        }
    }

    private static IEnumerable<PropertyInfo> GetStaticProperties(Type type)
    {
        DiagnosticClassAttribute diagAttr = GetAttribute<DiagnosticClassAttribute>(type, false);

        return type.GetProperties(PublicStaticPropertyFlags)
            .Where(propInfo => ShouldIncludeProperty(diagAttr, propInfo, null));
    }

    private static bool ShouldIncludeProperty(
        DiagnosticClassAttribute diagAttr,
        PropertyInfo info,
        TypeConfiguration typeConfiguration
    )
    {
        if (info.PropertyType == typeof(EventSink))
        {
            return false;
        }

        // An explicit Include or Exclude for this property outranks everything else, including
        // an attribute, because it is the most specific thing anyone said about it.
        if (typeConfiguration?.Find(info)?.Included is bool included)
        {
            return included;
        }

        bool attributedOnly = diagAttr is { AttributedPropertiesOnly: true };
        BrowsableAttribute browseAttr = GetAttribute<BrowsableAttribute>(info);
        DiagnosticPropertyAttribute propAttr = GetAttribute<DiagnosticPropertyAttribute>(info);

        if (propAttr != null)
        {
            return !propAttr.Ignore;
        }

        if (browseAttr is { Browsable: false })
        {
            return false;
        }

        // IncludeAll / ExcludeAll, which apply to everything this type did not speak about
        // individually. Placed after the attribute checks so an explicit [Browsable(false)] or
        // [DiagnosticProperty(Ignore = true)] still wins.
        if (typeConfiguration?.IncludeAll is bool includeAll)
        {
            return includeAll;
        }

        if (attributedOnly)
        {
            return browseAttr != null;
        }

        return true;
    }

    public static Type GetUnderlyingType(Type t)
    {
        Guard.NotNull(t, nameof(t));

        if (!t.IsGenericType)
        {
            return t;
        }

        if (t.GetGenericTypeDefinition() != typeof(Nullable<>))
        {
            return t;
        }

        return t.GetGenericArguments()[0];
    }

    private static T GetAttribute<T>(PropertyInfo info)
        where T : Attribute
    {
        object[] attrs = info.GetCustomAttributes(typeof(T), false);
        if (attrs.Length == 0)
        {
            return null;
        }

        return attrs[0] as T;
    }

    private static T GetAttribute<T>(Type info, bool inherit)
        where T : Attribute
    {
        object[] attrs = info.GetCustomAttributes(typeof(T), inherit);
        if (attrs.Length == 0)
        {
            return null;
        }

        return attrs[0] as T;
    }

    public static OperationResponse ExecuteOperation(string path, string operation, string[] arguments)
    {
        return ExecuteOperation(GetRegisteredObjects(), path, operation, arguments);
    }

    public static OperationResponse ExecuteOperation(
        IEnumerable<RegisteredObject> registeredObjects,
        string path,
        string operation,
        string[] arguments
    )
    {
        if (path == null)
        {
            return OperationResponse.Error("Object path not specified");
        }

        try
        {
            if (arguments == null)
            {
                arguments = [];
            }

            PropIdent ident = PropIdent.Parse(path);
            object sourceObject = GetSourceObject(registeredObjects, ident);
            OperationSet opSet = GetOperationSet(sourceObject);
            if (opSet == null)
            {
                throw new ArgumentException($"Can't find operations for {ident}");
            }

            Operation op = opSet.Operations.FirstOrDefault(x => x.Signature == operation);
            if (op == null)
            {
                throw new ArgumentException($"Operation '{operation}' not found");
            }

            ParameterInfo[] parameters = op.MethodInfo.GetParameters();

            if (parameters.Length != arguments.Length)
            {
                string msg =
                    $"Operation {operation} expected {parameters.Length} parameters, only found {arguments.Length}";
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
                    string msg =
                        $"Parameter {i + 1} ({parameters[i].Name}) can't convert '{arguments[i]}' to {TypeUtil.GetFriendlyTypeName(parameters[i].ParameterType)}";
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
        {
            return null;
        }

        if (obj is string str)
        {
            return str;
        }

        if (obj is not IEnumerable asEnumerable)
        {
            return Convert.ToString(obj);
        }

        string[] values = [.. asEnumerable.Cast<object>().Select(Convert.ToString)];
        if (values.Length == 0)
        {
            return "<Empty>";
        }

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
                string msg =
                    $"Can't invoke operation. Property bag {ident.BagCategory}|{ident.BagName} doesn't have a value.";
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
                string msg =
                    $"Can't invoke operation. Category {ident.BagCategory}|{ident.BagName}|{ident.PropCategory} doesn't have a value.";
                throw new ArgumentException(msg);
            }
            return cat.ValueObject;
        }

        Property prop = cat.Properties.FindByName(ident.PropName);
        if (prop == null)
        {
            string msg =
                $"Can't invoke operation. Property {ident.BagCategory}|{ident.BagName}|{ident.PropCategory} not found.";
            throw new ArgumentException(msg);
        }

        if (prop.ValueObject == null)
        {
            string msg =
                $"Can't invoke operation. Property {ident.BagCategory}|{ident.BagName}|{ident.PropCategory} doesn't have a value.";
            throw new ArgumentException(msg);
        }

        return prop.ValueObject;
    }

    #region SetProperty

    public static OperationResponse SetProperty(string path, string value)
    {
        return SetProperty(GetRegisteredObjects(), path, value);
    }

    public static OperationResponse SetProperty(
        IEnumerable<RegisteredObject> registeredObjects,
        string path,
        string value
    )
    {
        try
        {
            if (path == null)
            {
                return OperationResponse.Error("Property path not specified");
            }

            PropIdent ident = PropIdent.Parse(path);
            RegisteredObject regObj = registeredObjects.FindByCategoryAndName(ident.BagCategory, ident.BagName);
            if (regObj == null)
            {
                return OperationResponse.Error($"Can't find PropertyBag {ident.BagCategory}.{ident.BagName}");
            }

            object obj = regObj.Object;
            if (obj == null)
            {
                return OperationResponse.Error(
                    $"PropertyBag {ident.BagCategory}.{ident.BagName} was garbage collected just before I could set the property.  How bizarre!"
                );
            }

            List<PropertyGetter> valueGetters = GetPropertyGetters(obj);
            PropertyGetter getter = valueGetters.FirstOrDefault(g =>
                _ignoreCase.Equals(g.Name, ident.PropName)
                && _ignoreCase.Equals(g.Category ?? "", ident.PropCategory ?? "")
            );

            if (getter == null)
            {
                return OperationResponse.Error($"Can't find property [{ident.PropCategory}].[{ident.PropName}]");
            }

            if (!getter.CanSet)
            {
                return OperationResponse.Error(
                    $"You are not allowed to set [{ident.PropCategory}].[{ident.PropName}], AllowSet is not enabled!"
                );
            }

            bool isType = obj is Type;
            Type declaringType = getter.PropInfo.DeclaringType;
            if (!isType && (declaringType == null || !declaringType.IsInstanceOfType(obj)))
            {
                return OperationResponse.Error(
                    $"'{ident.PropCategory}'.'{ident.PropName}' property {getter.PropInfo.Name} expects type {declaringType?.Name ?? "<unknown>"}, got {obj.GetType().Name}"
                );
            }

            object newValue = ConvertValue(getter.PropInfo.PropertyType, value);
            getter.PropInfo.SetValue(isType ? null : obj, newValue, null);

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
        {
            throw new ArgumentException($"Can't find PropertyBag {ident.BagCategory}.{ident.BagName}");
        }

        object obj = regObj.Object;
        if (obj == null)
        {
            string msg =
                $"PropertyBag {ident.BagCategory}.{ident.BagName} was garbage collected just before I could set the property.  How bizarre!";
            throw new ArgumentException(msg);
        }

        return ObjectToPropertyBag(obj, ident.BagName, ident.BagCategory);
    }

    #endregion

    private sealed class PropIdent
    {
        public string BagCategory { get; private set; }
        public string BagName { get; private set; }
        public string PropCategory { get; private set; }
        public string PropName { get; private set; }

        public static PropIdent Parse(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path cannot be empty.");
            }

            string[] elements = path.Split('|');
            if (elements.Length < 2 || string.IsNullOrEmpty(elements[0]) || string.IsNullOrEmpty(elements[1]))
            {
                throw new ArgumentException(
                    $"Invalid property/operation path: '{path}'. Path must contain at least a category and name, separated by '|'."
                );
            }

            PropIdent ident = new()
            {
                BagCategory = NullIfEmpty(elements.ElementAtOrDefault(0)),
                BagName = NullIfEmpty(elements.ElementAtOrDefault(1)),
                PropCategory = NullIfEmpty(elements.ElementAtOrDefault(2)),
                PropName = NullIfEmpty(elements.ElementAtOrDefault(3)),
            };
            return ident;
        }

        public override string ToString()
        {
            if (PropName != null)
            {
                return $"{BagCategory}|{BagName}|{PropCategory}|{PropName}";
            }

            if (PropCategory != null)
            {
                return $"{BagCategory}|{BagName}|{PropCategory}";
            }

            return $"{BagCategory}|{BagName}";
        }

        private static string NullIfEmpty(string s)
        {
            return string.IsNullOrEmpty(s) ? null : s;
        }
    }

    private static object ConvertValue(Type type, string value)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            type = type.GetGenericArguments()[0];
        }

        if (type.IsEnum)
        {
            return Enum.Parse(type, value, true);
        }

        try
        {
            return Convert.ChangeType(value, type);
        }
        catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
        {
            if (TryParseValue(type, value, out object parsed))
            {
                return parsed;
            }

            throw;
        }
    }

    private static bool TryParseValue(Type type, string value, out object parsed)
    {
        parsed = null;

        MethodInfo method = type.GetMethod("Parse", PublicStaticMethods, null, [typeof(string)], null);

        if (method == null)
        {
            return false;
        }

        try
        {
            parsed = method.Invoke(null, [value]);
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
