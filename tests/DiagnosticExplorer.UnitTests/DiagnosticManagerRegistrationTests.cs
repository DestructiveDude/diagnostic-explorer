using AwesomeAssertions;
using DiagnosticExplorer.Props;

namespace DiagnosticExplorer.UnitTests;

/// <summary>
///     DiagnosticManager.Register is a static registry, and MakeNameUnique
///     (DiagnosticManager.cs:75) is the only thing standing between two components registering the
///     same bag name and the viewer silently merging them. The uniquification is deterministic —
///     a case-insensitive " N" suffix starting at 2, scoped per bag category, excluding the object
///     being (re-)registered. (DE-25)
/// </summary>
/// <remarks>
///     These tests mutate the process-wide registry, so every registration is unwound with
///     Unregister in a finally. xUnit runs tests within a single class sequentially, and no other
///     test class in this assembly touches the static registry, so the distinctive bag names below
///     cannot collide with a parallel test.
/// </remarks>
public class DiagnosticManagerRegistrationTests
{
    private static string RegisteredBagName(object obj, string category)
    {
        return DiagnosticManager
            .GetRegisteredObjects()
            .Where(ro => ReferenceEquals(ro.Object, obj) && ro.BagCategory == category)
            .Select(ro => ro.BagName)
            .Single();
    }

    /// <summary>
    ///     Two objects colliding on a bag name within one category are uniquified in registration
    ///     order: the first keeps the name, later colliders get deterministic " 2", " 3" suffixes.
    /// </summary>
    [Fact]
    public void Register_CollidingBagNames_AreUniquifiedDeterministically()
    {
        const string category = "DE25-Uniqueness";
        object first = new();
        object second = new();
        object third = new();

        try
        {
            DiagnosticManager.Register(first, "DE25 Bag", category);
            DiagnosticManager.Register(second, "DE25 Bag", category);
            DiagnosticManager.Register(third, "DE25 Bag", category);

            RegisteredBagName(first, category).Should().Be("DE25 Bag");
            RegisteredBagName(second, category).Should().Be("DE25 Bag 2");
            RegisteredBagName(third, category).Should().Be("DE25 Bag 3");
        }
        finally
        {
            DiagnosticManager.Unregister(first);
            DiagnosticManager.Unregister(second);
            DiagnosticManager.Unregister(third);
        }
    }

    /// <summary>
    ///     The collision check is case-insensitive: "de25 bag" and "DE25 BAG" are the same slot, so
    ///     the second registration is suffixed rather than overwriting the first in the viewer.
    /// </summary>
    [Fact]
    public void Register_BagNameDifferingOnlyByCase_StillCollides()
    {
        const string category = "DE25-Case";
        object first = new();
        object second = new();

        try
        {
            DiagnosticManager.Register(first, "de25 bag", category);
            DiagnosticManager.Register(second, "DE25 BAG", category);

            RegisteredBagName(first, category).Should().Be("de25 bag");
            RegisteredBagName(second, category).Should().Be("DE25 BAG 2");
        }
        finally
        {
            DiagnosticManager.Unregister(first);
            DiagnosticManager.Unregister(second);
        }
    }

    /// <summary>
    ///     Uniqueness is scoped per bag category: the same bag name under a different category is a
    ///     different slot and keeps its name unsuffixed.
    /// </summary>
    [Fact]
    public void Register_SameBagNameInDifferentCategory_DoesNotCollide()
    {
        object first = new();
        object second = new();

        try
        {
            DiagnosticManager.Register(first, "DE25 Bag", "DE25-CatA");
            DiagnosticManager.Register(second, "DE25 Bag", "DE25-CatB");

            RegisteredBagName(first, "DE25-CatA").Should().Be("DE25 Bag");
            RegisteredBagName(second, "DE25-CatB").Should().Be("DE25 Bag");
        }
        finally
        {
            DiagnosticManager.Unregister(first);
            DiagnosticManager.Unregister(second);
        }
    }

    /// <summary>
    ///     Re-registering the same object must not collide with itself (the RegisteredObject being
    ///     updated is excluded from the taken-name scan), otherwise every renewal would walk the
    ///     suffix chain and the bag name would drift on each refresh.
    /// </summary>
    [Fact]
    public void Register_SameObjectAgain_KeepsItsOwnName()
    {
        const string category = "DE25-Reregister";
        object obj = new();
        object other = new();

        try
        {
            DiagnosticManager.Register(obj, "DE25 Bag", category);
            DiagnosticManager.Register(other, "DE25 Bag", category);
            DiagnosticManager.Register(obj, "DE25 Bag", category);

            RegisteredBagName(obj, category).Should().Be("DE25 Bag");
            RegisteredBagName(other, category).Should().Be("DE25 Bag 2");
        }
        finally
        {
            DiagnosticManager.Unregister(obj);
            DiagnosticManager.Unregister(other);
        }
    }
}
