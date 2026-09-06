namespace DiagnosticExplorer;

/// <summary>How much of a nested object is rendered into the parent's bag.</summary>
internal enum NestedPropertyRenderMode
{
    /// <summary>Every getter the nested object has.</summary>
    All,

    /// <summary>
    ///     Only the nested object's own uncategorised properties. Skips anything the nested object
    ///     files under a category of its own, so an inlined object contributes a flat set of values
    ///     rather than dragging its whole category structure into the parent.
    /// </summary>
    PrimaryOnly,
}

/// <summary>Renders one object's properties into another object's bag.</summary>
internal static class NestedPropertyRenderer
{
    public static void Render(object value, PropertyBag bag, string category, NestedPropertyRenderMode mode)
    {
        // The mode is loop-invariant, so it selects the sequence once rather than being re-tested
        // for every getter.
        IEnumerable<PropertyGetter> getters = DiagnosticManager.GetPropertyGetters(value);
        if (mode != NestedPropertyRenderMode.All)
        {
            getters = getters.Where(getter => getter.IsDirectProperty && getter.IsInGeneralCategory(value));
        }

        foreach (PropertyGetter getter in getters)
        {
            getter.GetProperties(value, bag, category);
        }
    }
}
