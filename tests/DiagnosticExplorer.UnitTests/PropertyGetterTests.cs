using System.Collections;
using AwesomeAssertions;
using DiagnosticExplorer.Interface;
using DiagnosticExplorer.Props;

namespace DiagnosticExplorer.UnitTests;

/// <summary>
/// The property-getter pipeline (driven through DiagnosticManager.ObjectToPropertyBag) must be
/// resilient: a single throwing property degrades to an error string rather than aborting the
/// whole diagnostic walk, and a collection source is enumerated only once.
/// </summary>
public class PropertyGetterTests
{
    private static IEnumerable<string?> AllValues(PropertyBag bag)
        => bag.Categories.SelectMany(c => c.Properties).Select(p => p.Value);

    private sealed class ThrowingRate
    {
        [RateProperty]
        public RateCounter Boom => throw new InvalidOperationException("rate boom");
    }

    /// <summary>
    /// RateGetter read the RateCounter via the raw getter, outside the guarded GetValue path, so a
    /// throwing rate property aborted the entire walk. It now degrades to an error-string property. (M18)
    /// </summary>
    [Fact]
    public void RateProperty_ThatThrows_DegradesToErrorProperty_WithoutAbortingTheWalk()
    {
        PropertyBag bag = DiagnosticManager.ObjectToPropertyBag(new ThrowingRate(), "svc", null);

        AllValues(bag).Should().Contain(v => v != null && v.Contains("rate boom"));
    }

    private sealed class ThrowingDate
    {
        [DateProperty(ExposeDate = false, ExposeElapsed = true)]
        public DateTime Boom => throw new InvalidOperationException("date boom");
    }

    /// <summary>
    /// DateGetter's elapsed/until sub-path called the raw getter outside any try/catch; a throwing
    /// date property now degrades to an error string instead of aborting the walk. (M18)
    /// </summary>
    [Fact]
    public void DateProperty_ElapsedPath_ThatThrows_DegradesToErrorProperty()
    {
        PropertyBag bag = DiagnosticManager.ObjectToPropertyBag(new ThrowingDate(), "svc", null);

        AllValues(bag).Should().Contain(v => v != null && v.Contains("date boom"));
    }

    private sealed class CountingEnumerable : IEnumerable<int>
    {
        public int Enumerations;

        public IEnumerator<int> GetEnumerator()
        {
            Enumerations++;
            return Enumerable.Range(1, 3).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class HasConcatCollection
    {
        // Public field (not a property) so the diagnostic walk ignores it but the test can read it.
        public readonly CountingEnumerable Items = new();

        [CollectionProperty(CollectionMode.Concatenate)]
        public CountingEnumerable Numbers => Items;
    }

    /// <summary>
    /// CollectionGetter counted the source then re-enumerated it (concatenate mode up to three
    /// passes via FormatEnumerable's Count()+Take()), re-running stateful/expensive sequences. It
    /// now materializes once, so the source is enumerated exactly once. (M19)
    /// </summary>
    [Fact]
    public void ConcatenateCollection_EnumeratesTheSourceOnce()
    {
        var obj = new HasConcatCollection();

        DiagnosticManager.ObjectToPropertyBag(obj, "svc", null);

        obj.Items.Enumerations.Should().Be(1);
    }
}
