using AwesomeAssertions;
using DiagnosticExplorer.Util;

namespace DiagnosticExplorer.UnitTests;

/// <summary>
///     WeakReferenceHash is the name-keyed, case-insensitive registry behind the live
///     EventSink set: it must let entries be reclaimed by GC while still giving correct
///     add/lookup/remove semantics for entries that are kept alive. These tests hold strong
///     references throughout, so they pin the dictionary behaviour without depending on GC
///     timing (the weak-collection path is non-deterministic and deliberately out of scope).
/// </summary>
public class WeakReferenceHashTests
{
    /// <summary>
    ///     The core round-trip: an added, still-referenced item is reported present and returned
    ///     as the same instance — the registry's whole purpose.
    /// </summary>
    [Fact]
    public void Add_ThenGetItem_ReturnsSameInstanceAndReportsPresent()
    {
        var hash = new WeakReferenceHash<Item>();
        var item = new Item { Tag = "a" };

        hash.Add("alpha", item);

        hash.ContainsName("alpha").Should().BeTrue();
        hash.GetItem("alpha").Should().BeSameAs(item);
        hash.GetItem("alpha")!.Tag.Should().Be("a");
    }

    /// <summary>
    ///     Keys are matched case-insensitively (CurrentCultureIgnoreCase), so a differently-cased
    ///     lookup finds the same entry — the property EventSink relies on when sinks are addressed
    ///     by name regardless of casing.
    /// </summary>
    [Fact]
    public void Lookup_IsCaseInsensitive()
    {
        var hash = new WeakReferenceHash<Item>();
        var item = new Item();
        hash.Add("Alpha", item);

        hash.ContainsName("alpha").Should().BeTrue();
        hash.GetItem("ALPHA").Should().BeSameAs(item);
    }

    /// <summary>
    ///     Adding a second entry under an existing name is a programming error and throws, rather
    ///     than silently overwriting a live registration.
    /// </summary>
    [Fact]
    public void Add_DuplicateName_Throws()
    {
        var hash = new WeakReferenceHash<Item>();
        hash.Add("alpha", new Item());

        var act = () => hash.Add("alpha", new Item());

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Null name or null object are caller errors; both surface ArgumentNullException so the
    ///     failure is diagnosable at the call site. Parameterized over the two null arguments.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Add_WithNullArgument_Throws(bool nullName)
    {
        var hash = new WeakReferenceHash<Item>();

        var act = () => hash.Add(nullName ? null! : "alpha", nullName ? new Item() : null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    ///     GetItem with a factory is the get-or-create primitive: it invokes the factory exactly
    ///     once for a missing name, stores the result, and returns the same instance on the next
    ///     call without invoking the factory again.
    /// </summary>
    [Fact]
    public void GetItem_WithFactory_CreatesOnceAndCaches()
    {
        var hash = new WeakReferenceHash<Item>();
        var created = 0;

        var first = hash.GetItem(
            "alpha",
            () =>
            {
                created++;
                return new Item();
            }
        );
        var second = hash.GetItem(
            "alpha",
            () =>
            {
                created++;
                return new Item();
            }
        );

        created.Should().Be(1);
        second.Should().BeSameAs(first);
    }

    /// <summary>
    ///     A missing name with no factory returns null (not throwing), and Remove makes a present
    ///     entry absent again — the lifecycle a disposed EventSink drives via Remove.
    /// </summary>
    [Fact]
    public void GetItem_MissingWithoutFactory_IsNull_AndRemove_DropsEntry()
    {
        var hash = new WeakReferenceHash<Item>();
        hash.Add("alpha", new Item());

        hash.GetItem("missing").Should().BeNull();

        hash.Remove("alpha");
        hash.ContainsName("alpha").Should().BeFalse();
        hash.GetItem("alpha").Should().BeNull();
    }

    /// <summary>
    ///     GetItems returns every live entry — the enumeration the purge timer walks. With strong
    ///     references held, all added items are returned.
    /// </summary>
    [Fact]
    public void GetItems_ReturnsAllLiveEntries()
    {
        var hash = new WeakReferenceHash<Item>();
        var a = new Item();
        var b = new Item();
        hash.Add("a", a);
        hash.Add("b", b);

        hash.GetItems().Should().BeEquivalentTo(new[] { a, b });
    }

    private sealed record Item(string Tag = "");
}
