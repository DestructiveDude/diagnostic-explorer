using System;
using System.IO;
using System.IO.Compression;
using ProtoBuf;

namespace DiagnosticExplorer.Util;

public static class ProtobufUtil
{

    public static byte[] Compress<T>(T obj, int compressThreshold)
    {
        MemoryStream stream = new();
        stream.WriteByte(0);
        ProtoBuf.Serializer.Serialize(stream, obj);

        if (stream.Length <= compressThreshold)
        {
            return stream.ToArray();
        }

        MemoryStream compressedStream = new();
        compressedStream.WriteByte(1);
        using (GZipStream gstream = new(compressedStream, CompressionMode.Compress, leaveOpen: true))
        {
            stream.Position = 1;
            stream.CopyTo(gstream);
        }
        byte[] compressed = compressedStream.ToArray();

        return compressed;
    }


    // Ceiling on a decompressed payload, so a small highly-compressible (zip-bomb) input
    // can't inflate to exhaust memory.
    private const long MaxDecompressedBytes = 64L * 1024 * 1024;

    public static T Decompress<T>(byte[] body)
    {
        if (body == null || body.Length < 1)
        {
            throw new ArgumentException("Decompress requires a non-empty payload with a leading mode byte.", nameof(body));
        }

        using MemoryStream bodyStream = new(body, 1, body.Length - 1);

        if (body[0] == 0)
        {
            return Serializer.Deserialize<T>(bodyStream);
        }

        using GZipStream gstream = new(bodyStream, CompressionMode.Decompress);
        using LimitedReadStream limited = new(gstream, MaxDecompressedBytes);
        return Serializer.Deserialize<T>(limited);
    }

    /// <summary>
    /// Read-only stream wrapper that throws once more than <c>maxBytes</c> have been read,
    /// bounding the cost of decompressing a hostile/oversized payload.
    /// </summary>
    private sealed class LimitedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _maxBytes;
        private long _read;

        public LimitedReadStream(Stream inner, long maxBytes)
        {
            _inner = inner;
            _maxBytes = maxBytes;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = _inner.Read(buffer, offset, count);
            _read += n;
            if (_read > _maxBytes)
            {
                throw new InvalidDataException($"Decompressed payload exceeds the {_maxBytes} byte limit.");
            }

            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}