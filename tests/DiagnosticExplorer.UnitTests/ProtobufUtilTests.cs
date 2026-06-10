using AwesomeAssertions;
using DiagnosticExplorer.Interface;
using DiagnosticExplorer.Util;

namespace DiagnosticExplorer.UnitTests;

/// <summary>
/// ProtobufUtil is the wire transport: every diagnostic payload is round-tripped
/// through Compress/Decompress. A leading marker byte distinguishes raw (0) from
/// gzip-compressed (1) bodies, and the threshold decides which is used.
/// </summary>
public class ProtobufUtilTests
{
    private static PropertyBag SampleBag()
    {
        var bag = new PropertyBag("svc", "cat");
        bag.AddProperty(new Property("Uptime", "42"), "Stats");
        return bag;
    }

    /// <summary>
    /// A payload at or under the threshold is stored uncompressed (marker 0) and must
    /// round-trip unchanged. This is the common small-message path and proves the
    /// marker-byte framing and protobuf contract line up.
    /// </summary>
    [Fact]
    public void CompressThenDecompress_SmallPayload_StaysUncompressedAndRoundTrips()
    {
        var bytes = ProtobufUtil.Compress(SampleBag(), compressThreshold: 100_000);

        bytes[0].Should().Be(0, "a payload under the threshold is left uncompressed");
        var restored = ProtobufUtil.Decompress<PropertyBag>(bytes);

        restored.Name.Should().Be("svc");
        restored.GetProperty("Uptime", "Stats")!.Value.Should().Be("42");
    }

    /// <summary>
    /// A payload over the threshold is gzip-compressed (marker 1) and must still
    /// round-trip to an equal object — verifying the compress/decompress branch the
    /// large-message path takes.
    /// </summary>
    [Fact]
    public void CompressThenDecompress_OverThreshold_IsCompressedAndRoundTrips()
    {
        var bytes = ProtobufUtil.Compress(SampleBag(), compressThreshold: 0);

        bytes[0].Should().Be(1, "a payload over the threshold is gzip-compressed");
        var restored = ProtobufUtil.Decompress<PropertyBag>(bytes);

        restored.Name.Should().Be("svc");
        restored.GetProperty("Uptime", "Stats")!.Value.Should().Be("42");
    }
}
