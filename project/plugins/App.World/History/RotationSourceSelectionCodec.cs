using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FantaSim.World.TruthStream;
using TimeDete.Time.Primitives;

namespace FantaSim.App.World.History;

/// <summary>Canonical app-owned active rotation-source selection event types.</summary>
internal static class RotationSourceSelectionEventTypes
{
    public const string Imported = "app.rotation-source-imported.v1";
    public const string Generated = "app.rotation-source-generated.v1";
}

/// <summary>
/// Small, length-framed, big-endian codec for the durable active-source index. Imported markers
/// contain only the exact bound control cursor; the referenced control and plate events remain the
/// canonical truth and are always reread during recovery.
/// </summary>
internal static class RotationSourceSelectionCodec
{
    private const int SchemaVersion = 1;

    public static byte[] EncodeImported(TruthEventCursor boundCursor)
    {
        if (!boundCursor.IsBounded)
            throw new ArgumentException("Imported selection requires a bounded control cursor.", nameof(boundCursor));

        using var stream = new MemoryStream();
        WriteInt32(stream, SchemaVersion);
        WriteString(stream, boundCursor.Stream.VariantId);
        WriteString(stream, boundCursor.Stream.BranchId);
        WriteInt32(stream, boundCursor.Stream.LLevel);
        WriteString(stream, boundCursor.Stream.Domain);
        WriteString(stream, boundCursor.Stream.Model);
        WriteInt64(stream, boundCursor.Sequence);
        WriteBytes(stream, boundCursor.EventHash.Span);
        WriteInt64(stream, boundCursor.Tick.Value);
        return stream.ToArray();
    }

    public static TruthEventCursor DecodeImported(ReadOnlyMemory<byte> payload)
    {
        var bytes = payload.Span;
        var offset = 0;
        RequireVersion(bytes, ref offset);
        var stream = new TruthStreamIdentity(
            ReadString(bytes, ref offset),
            ReadString(bytes, ref offset),
            ReadInt32(bytes, ref offset),
            ReadString(bytes, ref offset),
            ReadString(bytes, ref offset));
        var sequence = ReadInt64(bytes, ref offset);
        var hash = ReadBytes(bytes, ref offset);
        var tick = new CanonicalTick(ReadInt64(bytes, ref offset));
        RequireEnd(bytes, offset);
        if (sequence < 0 || hash.Length != SHA256.HashSizeInBytes)
            throw new InvalidOperationException("Imported selection cursor sequence/hash is invalid.");
        return new TruthEventCursor(stream, sequence, hash, tick);
    }

    public static byte[] EncodeGenerated()
    {
        var payload = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(payload, SchemaVersion);
        return payload;
    }

    public static void ValidateGenerated(ReadOnlyMemory<byte> payload)
    {
        var bytes = payload.Span;
        var offset = 0;
        RequireVersion(bytes, ref offset);
        RequireEnd(bytes, offset);
    }

    private static void RequireVersion(ReadOnlySpan<byte> bytes, ref int offset)
    {
        if (ReadInt32(bytes, ref offset) != SchemaVersion)
            throw new InvalidOperationException("Unsupported rotation-source selection schema version.");
    }

    private static void WriteString(Stream stream, string value)
        => WriteBytes(stream, Encoding.UTF8.GetBytes(value));

    private static string ReadString(ReadOnlySpan<byte> bytes, ref int offset)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(ReadBytes(bytes, ref offset));
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidOperationException("Rotation-source selection contains invalid UTF-8.", ex);
        }
    }

    private static void WriteBytes(Stream stream, ReadOnlySpan<byte> value)
    {
        WriteInt32(stream, value.Length);
        stream.Write(value);
    }

    private static byte[] ReadBytes(ReadOnlySpan<byte> bytes, ref int offset)
    {
        var length = ReadInt32(bytes, ref offset);
        if (length < 0 || length > bytes.Length - offset)
            throw new InvalidOperationException("Rotation-source selection has an invalid length frame.");
        var result = bytes.Slice(offset, length).ToArray();
        offset += length;
        return result;
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static int ReadInt32(ReadOnlySpan<byte> bytes, ref int offset)
    {
        if (bytes.Length - offset < sizeof(int))
            throw new InvalidOperationException("Rotation-source selection payload is truncated.");
        var value = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(offset, sizeof(int)));
        offset += sizeof(int);
        return value;
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static long ReadInt64(ReadOnlySpan<byte> bytes, ref int offset)
    {
        if (bytes.Length - offset < sizeof(long))
            throw new InvalidOperationException("Rotation-source selection payload is truncated.");
        var value = BinaryPrimitives.ReadInt64BigEndian(bytes.Slice(offset, sizeof(long)));
        offset += sizeof(long);
        return value;
    }

    private static void RequireEnd(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset != bytes.Length)
            throw new InvalidOperationException("Rotation-source selection payload has trailing bytes.");
    }
}
