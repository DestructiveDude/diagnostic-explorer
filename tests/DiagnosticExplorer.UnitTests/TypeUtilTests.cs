using AwesomeAssertions;
using DiagnosticExplorer.Util;

namespace DiagnosticExplorer.UnitTests;

/// <summary>
/// TypeUtil renders friendly type names (used when describing diagnostic properties
/// reflected off application objects) and decides nullability. It is internal, reached
/// here via InternalsVisibleTo, because the rendering rules are non-trivial — generics,
/// nullable shorthand, and C# primitive aliases all have special cases.
/// </summary>
public class TypeUtilTests
{
    /// <summary>
    /// GetFriendlyTypeName maps primitives to their C# aliases, collapses Nullable&lt;T&gt;
    /// to the "T?" shorthand, and recursively renders open generics with their arguments.
    /// Parameterizing the cases pins each branch (alias table, nullable, single- and
    /// multi-arg generics, and the fall-through to Type.Name) in one place.
    /// </summary>
    [Theory]
    [InlineData(typeof(int), "int")]
    [InlineData(typeof(string), "string")]
    [InlineData(typeof(double), "double")]
    [InlineData(typeof(decimal), "decimal")]
    [InlineData(typeof(int?), "int?")]
    [InlineData(typeof(System.DateTime), "DateTime")]
    [InlineData(typeof(List<int>), "List<int>")]
    [InlineData(typeof(Dictionary<string, int>), "Dictionary<string, int>")]
    [InlineData(typeof(List<int?>), "List<int?>")]
    public void GetFriendlyTypeName_ForVariousTypes_RendersExpectedName(System.Type type, string expected)
    {
        TypeUtil.GetFriendlyTypeName(type).Should().Be(expected);
    }

    /// <summary>
    /// IsNullable is true only for Nullable&lt;T&gt; value types — not for plain value
    /// types nor for reference types (which are "nullable" in a different sense the method
    /// deliberately does not report). Covers all three branches together.
    /// </summary>
    [Theory]
    [InlineData(typeof(int?), true)]
    [InlineData(typeof(int), false)]
    [InlineData(typeof(string), false)]
    public void IsNullable_ForVariousTypes_ReturnsExpected(System.Type type, bool expected)
    {
        TypeUtil.IsNullable(type).Should().Be(expected);
    }

    /// <summary>
    /// The documented contract is to throw ArgumentNullException on a null type rather than
    /// dereference it — this keeps the failure at the caller, not deep in reflection.
    /// </summary>
    [Fact]
    public void IsNullable_WithNull_Throws()
    {
        var act = () => TypeUtil.IsNullable(null!);

        act.Should().Throw<System.ArgumentNullException>();
    }
}
