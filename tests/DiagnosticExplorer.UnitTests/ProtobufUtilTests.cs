using System.IO.Compression;
using AwesomeAssertions;
using DiagnosticExplorer.Interface;
using DiagnosticExplorer.Util;
using ProtoBuf;

namespace DiagnosticExplorer.UnitTests;

/// <summary>
///     ProtobufUtil is the wire transport: every diagnostic payload is round-tripped
///     through Compress/Decompress. A leading marker byte distinguishes raw (0) from
///     gzip-compressed (1) bodies, and the threshold decides which is used.
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
    ///     A payload at or under the threshold is stored uncompressed (marker 0) and must
    ///     round-trip unchanged. This is the common small-message path and proves the
    ///     marker-byte framing and protobuf contract line up.
    /// </summary>
    [Fact]
    public void CompressThenDecompress_SmallPayload_StaysUncompressedAndRoundTrips()
    {
        var bytes = ProtobufUtil.Compress(SampleBag(), 100_000);

        bytes[0].Should().Be(0, "a payload under the threshold is left uncompressed");
        var restored = ProtobufUtil.Decompress<PropertyBag>(bytes);

        restored.Name.Should().Be("svc");
        restored.GetProperty("Uptime", "Stats")!.Value.Should().Be("42");
    }

    /// <summary>
    ///     A payload over the threshold is gzip-compressed (marker 1) and must still
    ///     round-trip to an equal object — verifying the compress/decompress branch the
    ///     large-message path takes.
    /// </summary>
    [Fact]
    public void CompressThenDecompress_OverThreshold_IsCompressedAndRoundTrips()
    {
        var bytes = ProtobufUtil.Compress(SampleBag(), 0);

        bytes[0].Should().Be(1, "a payload over the threshold is gzip-compressed");
        var restored = ProtobufUtil.Decompress<PropertyBag>(bytes);

        restored.Name.Should().Be("svc");
        restored.GetProperty("Uptime", "Stats")!.Value.Should().Be("42");
    }

    /// <summary>
    ///     Regression (DE-6): a small, highly-compressible gzip body from a public hub
    ///     RPC could otherwise inflate to gigabytes and exhaust server memory, so
    ///     Decompress caps decompressed reads at 64 MB. This hand-frames a <i>valid</i>
    ///     protobuf payload — one length-delimited string field that genuinely
    ///     decompresses past the cap — and asserts it is rejected with an
    ///     InvalidDataException (protobuf-net may surface it as an inner exception).
    ///     Raising MaxDecompressedBytes above the payload size makes this test go red.
    /// </summary>
    [Fact]
    public void Decompress_ZipBombOverCap_ThrowsInvalidDataException()
    {
        var payload = BuildZipBomb();

        Exception? ex = Record.Exception(() => ProtobufUtil.Decompress<StringHolder>(payload));

        ex.Should().NotBeNull("the payload inflates past the 64 MB decompressed-size cap");
        ExceptionChain(ex!)
            .Should()
            .Contain(
                e => e is InvalidDataException,
                "LimitedReadStream must reject the payload once it inflates past the cap"
            );
    }

    /// <summary>
    ///     Marker byte 1 + gzip of a valid length-delimited protobuf string field of
    ///     ~70 MB of zeros. Written in small chunks so test memory stays flat; the
    ///     highly repetitive content compresses to a few dozen KB, mirroring a real
    ///     zip bomb. Zeros alone are not enough — the field must be well-formed or
    ///     protobuf-net would fail on the framing long before the 64 MB cap is hit.
    /// </summary>
    private static byte[] BuildZipBomb()
    {
        const int fieldSize = 70 * 1024 * 1024;

        using var output = new MemoryStream();
        output.WriteByte(1); // marker: gzip-compressed body
        using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
        {
            gzip.WriteByte(0x0A); // protobuf field 1, wire type 2 (length-delimited)
            WriteVarint(gzip, fieldSize);

            var chunk = new byte[64 * 1024];
            for (var remaining = fieldSize; remaining > 0; remaining -= chunk.Length)
            {
                gzip.Write(chunk, 0, Math.Min(chunk.Length, remaining));
            }
        }

        return output.ToArray();
    }

    private static void WriteVarint(Stream stream, uint value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        stream.WriteByte((byte)value);
    }

    private static IEnumerable<Exception> ExceptionChain(Exception ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            yield return current;
        }
    }

    [ProtoContract]
    private sealed class StringHolder
    {
        [ProtoMember(1)]
        public string Value { get; set; } = "";
    }
}
