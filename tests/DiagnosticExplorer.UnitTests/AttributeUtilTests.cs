using AwesomeAssertions;
using DiagnosticExplorer.Util;

namespace DiagnosticExplorer.UnitTests;

/// <summary>
///     AttributeUtil is the reflection helper the property-discovery pipeline uses to read the
///     DiagnosticClass/Property/Method attributes off application types. These tests confirm it
///     finds an attribute on each member kind (type, property, method) and returns null — rather
///     than throwing — when the attribute is absent, which is the common "not decorated" case.
/// </summary>
public class AttributeUtilTests
{
    /// <summary>
    ///     GetAttribute resolves the attribute from a Type, PropertyInfo, and MethodInfo — the
    ///     three overloads the discovery code calls. Reading the Tag back proves it returned the
    ///     right instance for each member kind.
    /// </summary>
    [Fact]
    public void GetAttribute_FindsMarkerOnTypePropertyAndMethod()
    {
        AttributeUtil.GetAttribute<MarkerAttribute>(typeof(Sample)).Tag.Should().Be("type");

        var prop = typeof(Sample).GetProperty(nameof(Sample.Value))!;
        AttributeUtil.GetAttribute<MarkerAttribute>(prop).Tag.Should().Be("prop");

        var method = typeof(Sample).GetMethod(nameof(Sample.Do))!;
        AttributeUtil.GetAttribute<MarkerAttribute>(method).Tag.Should().Be("method");
    }

    /// <summary>
    ///     An undecorated member returns null, not an exception — the discovery code treats null
    ///     as "this member opts out", so probing every property must be cheap and safe.
    /// </summary>
    [Fact]
    public void GetAttribute_WhenAbsent_ReturnsNull()
    {
        var bare = typeof(Sample).GetProperty(nameof(Sample.Bare))!;

        AttributeUtil.GetAttribute<MarkerAttribute>(bare).Should().BeNull();
    }

    [AttributeUsage(AttributeTargets.All)]
    private sealed class MarkerAttribute : Attribute
    {
        public string Tag { get; set; } = "";
    }

    [Marker(Tag = "type")]
    private class Sample
    {
        [Marker(Tag = "prop")]
        public int Value { get; set; }

        public int Bare { get; set; } = 1;

        [Marker(Tag = "method")]
        public void Do()
        {
            Value++;
        }
    }
}
