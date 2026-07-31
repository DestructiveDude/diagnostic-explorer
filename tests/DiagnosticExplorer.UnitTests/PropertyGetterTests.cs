using System.Collections;
using AwesomeAssertions;
using DiagnosticExplorer.Interface;
using DiagnosticExplorer.Props;

// Properties in the nested fixtures are consumed through reflection by DiagnosticManager.
// ReSharper disable UnusedMember.Local

namespace DiagnosticExplorer.UnitTests;

/// <summary>
///     The property-getter pipeline (driven through DiagnosticManager.ObjectToPropertyBag) must be
///     resilient: a single throwing property degrades to an error string rather than aborting the
///     whole diagnostic walk, and a collection source is enumerated only once.
/// </summary>
public class PropertyGetterTests
{
    private static IEnumerable<string?> AllValues(PropertyBag bag)
    {
        return bag.Categories.SelectMany(c => c.Properties).Select(p => p.Value);
    }

    private static IEnumerable<string?> AllNames(PropertyBag bag)
    {
        return bag.Categories.SelectMany(c => c.Properties).Select(p => p.Name);
    }

    /// <summary>
    ///     RateGetter read the RateCounter via the raw getter, outside the guarded GetValue path, so a
    ///     throwing rate property aborted the entire walk. It now degrades to an error-string property. (M18)
    /// </summary>
    [Fact]
    public void RateProperty_ThatThrows_DegradesToErrorProperty_WithoutAbortingTheWalk()
    {
        var bag = DiagnosticManager.ObjectToPropertyBag(new ThrowingRate(), "svc", null);

        AllValues(bag).Should().Contain(v => v != null && v.Contains("rate boom"));
    }

    /// <summary>
    ///     DateGetter's elapsed/until sub-path called the raw getter outside any try/catch; a throwing
    ///     date property now degrades to an error string instead of aborting the walk. (M18)
    /// </summary>
    [Fact]
    public void DateProperty_ElapsedPath_ThatThrows_DegradesToErrorProperty()
    {
        var bag = DiagnosticManager.ObjectToPropertyBag(new ThrowingDate(), "svc", null);

        AllValues(bag).Should().Contain(v => v != null && v.Contains("date boom"));
    }

    /// <summary>
    ///     CollectionGetter counted the source then re-enumerated it (concatenate mode up to three
    ///     passes via FormatEnumerable's Count()+Take()), re-running stateful/expensive sequences. It
    ///     now materializes once, so the source is enumerated exactly once. (M19)
    /// </summary>
    [Fact]
    public void ConcatenateCollection_EnumeratesTheSourceOnce()
    {
        var obj = new HasConcatCollection();

        DiagnosticManager.ObjectToPropertyBag(obj, "svc", null);

        obj.Items.Enumerations.Should().Be(1);
    }

    /// <summary>
    ///     CollectionGetter.AppendSeparateCategories must render a &lt;cycle&gt; placeholder for an
    ///     already-visited object instead of recursing into it. ObjectToPropertyBag seeds
    ///     VisitedObjects with the root, so a Categories-mode collection containing the root itself
    ///     trips the guard on the first item. Without the guard the walk recurses unboundedly and
    ///     dies to an uncatchable StackOverflowException — CollectionGetter's own catch at
    ///     CollectionGetter.cs:217-221 cannot mitigate that. The assertion is on Property.Name: the
    ///     placeholder carries no Value, so the AllValues probe cannot see it. (DE-7)
    /// </summary>
    [Fact]
    public void CategoriesCollection_ContainingSelf_RendersCyclePlaceholder_InsteadOfRecursing()
    {
        var bag = DiagnosticManager.ObjectToPropertyBag(new SelfContainingChildren(), "svc", null);

        Property? cycle = bag.GetProperty("<cycle>", "Item 0");
        cycle.Should().NotBeNull();
        cycle!.Name.Should().Be("<cycle>");
    }

    /// <summary>
    ///     CollectionGetter.AppendSeparateCategories must render a &lt;max depth&gt; placeholder once
    ///     more than 50 objects are on the VisitedObjects stack, instead of recursing deeper. A chain
    ///     longer than 50 nodes trips the guard; without it the walk would follow arbitrarily deep
    ///     object graphs. (DE-7)
    /// </summary>
    [Fact]
    public void CategoriesCollection_DeeperThan50_RendersMaxDepthPlaceholder_InsteadOfRecursing()
    {
        var bag = DiagnosticManager.ObjectToPropertyBag(BuildCollectionChain(60), "svc", null);

        AllNames(bag).Should().Contain("<max depth>");
    }

    /// <summary>
    ///     ExtendedPropertyGetter duplicates the cycle guard: an [ExtendedProperty] whose value is
    ///     already visited (here the root itself) must render a &lt;cycle&gt; placeholder instead of
    ///     recursing. (DE-7)
    /// </summary>
    [Fact]
    public void ExtendedProperty_ReferencingSelf_RendersCyclePlaceholder_InsteadOfRecursing()
    {
        var bag = DiagnosticManager.ObjectToPropertyBag(new ExtendedSelfReference(), "svc", null);

        Property? cycle = bag.GetProperty("<cycle>", "Self");
        cycle.Should().NotBeNull();
        cycle!.Name.Should().Be("<cycle>");
    }

    /// <summary>
    ///     ExtendedPropertyGetter duplicates the depth guard: an [ExtendedProperty] chain deeper than
    ///     50 visited objects must render a &lt;max depth&gt; placeholder instead of recursing. (DE-7)
    /// </summary>
    [Fact]
    public void ExtendedProperty_ChainDeeperThan50_RendersMaxDepthPlaceholder_InsteadOfRecursing()
    {
        var bag = DiagnosticManager.ObjectToPropertyBag(BuildExtendedChain(60), "svc", null);

        AllNames(bag).Should().Contain("<max depth>");
    }

    private static CollectionChainNode BuildCollectionChain(int depth)
    {
        CollectionChainNode? node = null;
        for (int i = 0; i < depth; i++)
        {
            node = new CollectionChainNode(node);
        }
        return node!;
    }

    private static ExtendedChainNode BuildExtendedChain(int depth)
    {
        ExtendedChainNode? node = null;
        for (int i = 0; i < depth; i++)
        {
            node = new ExtendedChainNode(node);
        }
        return node!;
    }

    /// <summary>
    ///     DateGetter's elapsed path must normalize UTC values — and Unspecified values on an
    ///     IsUTC-attributed property — to local time before diffing against DateTime.Now; without
    ///     it "Time since" is wrong by the local UTC offset while the throwing-branch test still
    ///     passes. Four properties hold the same instant in different kinds: all must render the
    ///     same elapsed value, and that value must be ~90 seconds, not off by an offset. The
    ///     equality assertion pins the normalization; the magnitude assertion pins that it is the
    ///     local offset being applied (a dropped ToLocalTime shifts the UTC value by the offset).
    ///     The two-string tolerance absorbs a second boundary crossing between the fixture's clock
    ///     read and the getter's. (DE-29)
    /// </summary>
    [Fact]
    public void DateProperty_UtcAndUnspecifiedKinds_AreNormalizedToLocalBeforeElapsed()
    {
        var bag = DiagnosticManager.ObjectToPropertyBag(new NormalizableDates(), "svc", null);

        var values = AllValues(bag).ToList();
        values.Should().HaveCount(4);
        values.Distinct().Should().ContainSingle();
        values[0].Should().BeOneOf("00:01:30", "00:01:31");
    }

    private sealed class ThrowingRate
    {
        [RateProperty]
        public RateCounter Boom => throw new InvalidOperationException("rate boom");
    }

    private sealed class ThrowingDate
    {
        [DateProperty(ExposeDate = false, ExposeElapsed = true)]
        public DateTime Boom => throw new InvalidOperationException("date boom");
    }

    private sealed class NormalizableDates
    {
        private readonly DateTime _instantUtc = DateTime.UtcNow.AddSeconds(-90);

        [DateProperty(ExposeDate = false, ExposeElapsed = true)]
        public DateTime UtcValue => _instantUtc;

        [DateProperty(ExposeDate = false, ExposeElapsed = true, IsUTC = true)]
        public DateTime UnspecifiedAsUtc => DateTime.SpecifyKind(_instantUtc, DateTimeKind.Unspecified);

        [DateProperty(ExposeDate = false, ExposeElapsed = true)]
        public DateTime LocalValue => _instantUtc.ToLocalTime();

        // Unspecified without IsUTC is taken at face value (already local) — must render the same.
        [DateProperty(ExposeDate = false, ExposeElapsed = true)]
        public DateTime UnspecifiedLocal =>
            DateTime.SpecifyKind(_instantUtc.ToLocalTime(), DateTimeKind.Unspecified);
    }

    private sealed class CountingEnumerable : IEnumerable<int>
    {
        public int Enumerations;

        public IEnumerator<int> GetEnumerator()
        {
            Enumerations++;
            return Enumerable.Range(1, 3).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    private sealed class HasConcatCollection
    {
        // Public field (not a property) so the diagnostic walk ignores it but the test can read it.
        public readonly CountingEnumerable Items = new();

        [CollectionProperty(CollectionMode.Concatenate)]
        public CountingEnumerable Numbers => Items;
    }

    private sealed class SelfContainingChildren
    {
        [CollectionProperty(CollectionMode.Categories)]
        public List<SelfContainingChildren> Children => [this];
    }

    private sealed class CollectionChainNode
    {
        private readonly CollectionChainNode? _next;

        public CollectionChainNode(CollectionChainNode? next)
        {
            _next = next;
        }

        [CollectionProperty(CollectionMode.Categories)]
        public List<CollectionChainNode> Children => _next is null ? [] : [_next];
    }

    private sealed class ExtendedSelfReference
    {
        [ExtendedProperty]
        public ExtendedSelfReference Self => this;
    }

    private sealed class ExtendedChainNode
    {
        public ExtendedChainNode(ExtendedChainNode? next)
        {
            Next = next;
        }

        [ExtendedProperty]
        public ExtendedChainNode? Next { get; }
    }
}
