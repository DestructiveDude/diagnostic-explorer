using System.IO.Compression;
using System.Reflection;
using AwesomeAssertions;
using DiagnosticExplorer.Events;
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
    ///     (DE-27) The hub wire path (DiagnosticClientHandler / DiagnosticHub.LogEvents) ships
    ///     <see cref="DiagnosticMsg" /> arrays and <see cref="DiagnosticResponse" /> graphs through
    ///     Compress/Decompress, yet those types were never round-tripped in tests. A populated
    ///     <see cref="DiagnosticMsg" /> array must survive both framing paths — raw (marker 0, at or
    ///     under the threshold) and gzip (marker 1, over it) — with every field intact.
    /// </summary>
    [Theory]
    [InlineData(1_000_000, 0)] // under threshold: raw framing
    [InlineData(0, 1)] // over threshold: gzip framing
    public void DiagnosticMsgArray_RoundTrips_WithFieldsIntact_OnBothMarkerPaths(
        int compressThreshold,
        byte expectedMarker
    )
    {
        DiagnosticMsg[] messages =
        [
            new()
            {
                Level = 2,
                Date = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                Machine = "SRV-01",
                Process = "OrderWorker",
                User = "svc-orders",
                Category = "Orders",
                Message = "Order accepted",
                Environment = "PROD",
                MsgId = "abc123",
            },
            new()
            {
                Level = 4,
                Date = new DateTime(2026, 6, 7, 8, 9, 10, DateTimeKind.Utc),
                Machine = "SRV-02",
                Process = "AuditWorker",
                User = "svc-audit",
                Category = "Audit",
                Message = "Write failed",
                Environment = "UAT",
                MsgId = "def456",
            },
        ];

        var bytes = ProtobufUtil.Compress(messages, compressThreshold);

        bytes[0].Should().Be(expectedMarker);
        DiagnosticMsg[] restored = ProtobufUtil.Decompress<DiagnosticMsg[]>(bytes);

        restored.Should().HaveCount(2);
        restored[0].Should().BeEquivalentTo(messages[0]);
        restored[1].Should().BeEquivalentTo(messages[1]);
    }

    /// <summary>
    ///     (DE-27) A fully populated <see cref="DiagnosticResponse" /> — property bags, an
    ///     <see cref="EventResponse" /> carrying <see cref="SystemEvent" />s, context and exception
    ///     strings, and an <see cref="OperationSet" /> graph — must survive compress → decompress
    ///     with every field intact, on both the raw and gzip marker paths.
    /// </summary>
    [Theory]
    [InlineData(1_000_000, 0)]
    [InlineData(0, 1)]
    public void DiagnosticResponse_RoundTrips_WithFieldsIntact_OnBothMarkerPaths(
        int compressThreshold,
        byte expectedMarker
    )
    {
        var bag = new PropertyBag("svc", "cat");
        bag.AddProperty(new Property("Uptime", "42"), "Stats");

        DiagnosticResponse response = new()
        {
            PropertyBags = [bag],
            Events =
            [
                new EventResponse("Retro Events", "Retro")
                {
                    Events =
                    [
                        new SystemEvent
                        {
                            Id = 99,
                            Date = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc),
                            Message = "search started",
                            Detail = "query 7",
                            Level = 3,
                            SinkName = "Retro Events",
                            SinkCategory = "Retro",
                        },
                    ],
                },
            ],
            Context = "TestCategory|TestBag",
            ExceptionMessage = "boom",
            ExceptionDetail = "stack-ish detail",
            OperationSets =
            [
                new OperationSet
                {
                    Id = "TestCategory|TestBag",
                    Operations =
                    [
                        new Operation
                        {
                            ReturnType = "void",
                            Signature = "Run(String)",
                            Description = "runs the thing",
                            Parameters = [new OperationParameter("arg", "String")],
                        },
                    ],
                },
            ],
        };

        var bytes = ProtobufUtil.Compress(response, compressThreshold);

        bytes[0].Should().Be(expectedMarker);
        DiagnosticResponse restored = ProtobufUtil.Decompress<DiagnosticResponse>(bytes);

        restored.Context.Should().Be("TestCategory|TestBag");
        restored.ExceptionMessage.Should().Be("boom");
        restored.ExceptionDetail.Should().Be("stack-ish detail");

        restored.PropertyBags.Should().HaveCount(1);
        restored.PropertyBags[0].Name.Should().Be("svc");
        restored.PropertyBags[0].GetProperty("Uptime", "Stats")!.Value.Should().Be("42");

        restored.Events.Should().HaveCount(1);
        restored.Events[0].Name.Should().Be("Retro Events");
        restored.Events[0].Category.Should().Be("Retro");
        restored.Events[0].Events.Should().HaveCount(1);
        restored.Events[0].Events[0].Should().BeEquivalentTo(response.Events[0].Events[0]);

        restored.OperationSets.Should().HaveCount(1);
        restored.OperationSets[0].Id.Should().Be("TestCategory|TestBag");
        restored.OperationSets[0].Operations.Should().HaveCount(1);
        Operation restoredOp = restored.OperationSets[0].Operations[0];
        restoredOp.ReturnType.Should().Be("void");
        restoredOp.Signature.Should().Be("Run(String)");
        restoredOp.Description.Should().Be("runs the thing");
        restoredOp.Parameters.Should().HaveCount(1);
        restoredOp.Parameters[0].Name.Should().Be("arg");
        restoredOp.Parameters[0].Type.Should().Be("String");
    }

    /// <summary>
    ///     (DE-27) Protobuf field numbers are the wire contract: renumbering a
    ///     <see cref="ProtoMemberAttribute" /> compiles clean on both ends but silently
    ///     cross-wires fields between client and service. Pin every ordinal of the types that
    ///     travel over the hub wire path. SystemEvent tag 5 is deliberately absent (the removed
    ///     Severity member — see the comment in SystemEvent.cs) and is pinned by the gap test
    ///     below.
    /// </summary>
    [Theory]
    [InlineData(typeof(DiagnosticResponse), nameof(DiagnosticResponse.PropertyBags), 1)]
    [InlineData(typeof(DiagnosticResponse), nameof(DiagnosticResponse.Events), 2)]
    [InlineData(typeof(DiagnosticResponse), nameof(DiagnosticResponse.Context), 3)]
    [InlineData(typeof(DiagnosticResponse), nameof(DiagnosticResponse.ExceptionMessage), 4)]
    [InlineData(typeof(DiagnosticResponse), nameof(DiagnosticResponse.ExceptionDetail), 5)]
    [InlineData(typeof(DiagnosticResponse), nameof(DiagnosticResponse.OperationSets), 6)]
    [InlineData(typeof(DiagnosticMsg), nameof(DiagnosticMsg.Level), 1)]
    [InlineData(typeof(DiagnosticMsg), nameof(DiagnosticMsg.Date), 2)]
    [InlineData(typeof(DiagnosticMsg), nameof(DiagnosticMsg.Machine), 3)]
    [InlineData(typeof(DiagnosticMsg), nameof(DiagnosticMsg.Process), 4)]
    [InlineData(typeof(DiagnosticMsg), nameof(DiagnosticMsg.User), 5)]
    [InlineData(typeof(DiagnosticMsg), nameof(DiagnosticMsg.Category), 6)]
    [InlineData(typeof(DiagnosticMsg), nameof(DiagnosticMsg.Message), 7)]
    [InlineData(typeof(DiagnosticMsg), nameof(DiagnosticMsg.Environment), 8)]
    [InlineData(typeof(DiagnosticMsg), nameof(DiagnosticMsg.MsgId), 9)]
    [InlineData(typeof(OperationSet), nameof(OperationSet.Id), 1)]
    [InlineData(typeof(OperationSet), nameof(OperationSet.Operations), 2)]
    [InlineData(typeof(Operation), nameof(Operation.ReturnType), 1)]
    [InlineData(typeof(Operation), nameof(Operation.Signature), 2)]
    [InlineData(typeof(Operation), nameof(Operation.Description), 3)]
    [InlineData(typeof(Operation), nameof(Operation.Parameters), 4)]
    [InlineData(typeof(OperationParameter), nameof(OperationParameter.Name), 1)]
    [InlineData(typeof(OperationParameter), nameof(OperationParameter.Type), 2)]
    [InlineData(typeof(EventResponse), nameof(EventResponse.Name), 1)]
    [InlineData(typeof(EventResponse), nameof(EventResponse.Category), 2)]
    [InlineData(typeof(EventResponse), nameof(EventResponse.Events), 3)]
    [InlineData(typeof(SystemEvent), nameof(SystemEvent.Id), 1)]
    [InlineData(typeof(SystemEvent), nameof(SystemEvent.Date), 2)]
    [InlineData(typeof(SystemEvent), nameof(SystemEvent.Message), 3)]
    [InlineData(typeof(SystemEvent), nameof(SystemEvent.Detail), 4)]
    [InlineData(typeof(SystemEvent), nameof(SystemEvent.Level), 6)]
    [InlineData(typeof(SystemEvent), nameof(SystemEvent.SinkName), 7)]
    [InlineData(typeof(SystemEvent), nameof(SystemEvent.SinkCategory), 8)]
    public void ProtoMemberOrdinals_MatchTheWireContract(Type type, string propertyName, int tag)
    {
        ProtoMemberAttribute? attr = type.GetProperty(propertyName)!
            .GetCustomAttribute<ProtoMemberAttribute>();

        attr.Should().NotBeNull($"{type.Name}.{propertyName} must carry an explicit ProtoMember");
        attr!.Tag.Should().Be(tag);
    }

    /// <summary>
    ///     (DE-27) SystemEvent tag 5 belonged to the removed Severity member and must stay unused:
    ///     reusing it would cross-wire new data into old clients' Severity slot. The gap itself is
    ///     the contract.
    /// </summary>
    [Fact]
    public void SystemEvent_Tag5_StaysUnused()
    {
        var usedTags = typeof(SystemEvent)
            .GetProperties()
            .Select(p => p.GetCustomAttribute<ProtoMemberAttribute>()?.Tag)
            .Where(t => t.HasValue)
            .ToArray();

        usedTags.Should().NotContain(5, "tag 5 was the removed Severity member and is reserved");
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
