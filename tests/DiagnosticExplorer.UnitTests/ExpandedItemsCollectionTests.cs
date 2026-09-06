using AwesomeAssertions;

// Fixture properties are consumed through reflection by DiagnosticManager.
// ReSharper disable UnusedMember.Local

namespace DiagnosticExplorer.UnitTests;

/// <summary>
///     CollectionMode.ExpandedItems renders one expanded category per item, each holding that
///     item's own properties inline rather than a nested value.
/// </summary>
/// <remarks>
///     The mode arrived with the getter-layer merge. Adding an enum member without a matching case
///     is a silent no-op — the property simply renders nothing — which is why the mode switch now
///     throws on an unhandled mode and why these assert on rendered output rather than on the
///     getter.
/// </remarks>
public class ExpandedItemsCollectionTests
{
    private static PropertyBag Bag(object obj) => DiagnosticManager.ObjectToPropertyBag(obj, "svc", null);

    /// <summary>Each item becomes its own category, holding that item's own properties.</summary>
    [Fact]
    public void ExpandedItems_RendersOneCategoryPerItem_HoldingTheItemsOwnProperties()
    {
        PropertyBag bag = Bag(new OrderBook());

        Category first = bag.Categories.FindByName("Orders.GBP");
        first.Should().NotBeNull();
        first.Properties.Select(p => p.Name).Should().Contain("Venue");
        first.Properties.Single(p => p.Name == "Venue").Value.Should().Be("LSE");

        bag.Categories.FindByName("Orders.USD").Should().NotBeNull();
    }

    /// <summary>
    ///     The parent category is marked expanded so the client opens it rather than showing a
    ///     collapsed node the operator has to hunt for.
    /// </summary>
    [Fact]
    public void ExpandedItems_MarksTheParentCategoryExpanded()
    {
        Category parent = Bag(new OrderBook()).Categories.FindByName("Orders");

        parent.Should().NotBeNull();
        parent.IsExpanded.Should().BeTrue();
        parent.IsExpandedProperty.Should().BeTrue();
    }

    /// <summary>
    ///     A self-referencing item must not recurse forever. Upstream's equivalent has no guard, so
    ///     this pins ours: the cycle is reported in place and the walk continues.
    /// </summary>
    [Fact]
    public void ExpandedItems_WithASelfReferencingItem_ReportsTheCycleInsteadOfRecursing()
    {
        var cyclic = new SelfReferencingHolder();

        PropertyBag bag = Bag(cyclic);

        bag.Categories.SelectMany(c => c.Properties).Select(p => p.Name).Should().Contain("<cycle>");
    }

    /// <summary>
    ///     An unhandled collection mode used to render nothing at all. It now throws, so a mode
    ///     added without a case fails loudly at the point of use instead of silently emitting an
    ///     empty property.
    /// </summary>
    [Fact]
    public void UnsupportedCollectionMode_ThrowsRatherThanRenderingNothing()
    {
        Property rendered = Bag(new UndefinedModeCarrier())
            .Categories.SelectMany(c => c.Properties)
            .Single(p => p.Name == "Items");

        rendered.Value.Should().StartWith("<").And.Contain("Unsupported collection mode");
    }

#pragma warning disable S1144, S2325
    private sealed class Order
    {
        public Order(string currency, string venue)
        {
            Currency = currency;
            Venue = venue;
        }

        public string Currency { get; }
        public string Venue { get; }
    }

    private sealed class OrderBook
    {
        [CollectionProperty(CollectionMode.ExpandedItems, "Orders", CategoryProperty = nameof(Order.Currency))]
        public List<Order> Orders { get; } = [new("GBP", "LSE"), new("USD", "NYSE")];
    }

    // A plain object-typed property renders as a value and never recurses, so a cycle needs a
    // nested expanded collection: rendering a node renders its children, one of which is the node.
    private sealed class SelfReferencingNode
    {
        public SelfReferencingNode(string name)
        {
            Name = name;
            Children = [this];
        }

        public string Name { get; }

        [CollectionProperty(CollectionMode.ExpandedItems, "Children", CategoryProperty = nameof(Name))]
        public List<SelfReferencingNode> Children { get; }
    }

    private sealed class SelfReferencingHolder
    {
        [CollectionProperty(CollectionMode.ExpandedItems, "Nodes", CategoryProperty = nameof(SelfReferencingNode.Name))]
        public List<SelfReferencingNode> Nodes { get; } = [new("loop")];
    }

    private sealed class UndefinedModeCarrier
    {
        [CollectionProperty((CollectionMode)999, "Items")]
        public List<string> Items { get; } = ["one"];
    }
#pragma warning restore S1144, S2325
}
