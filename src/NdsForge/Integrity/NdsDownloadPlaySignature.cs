namespace NdsForge;

/// <summary>Retains a stored Download Play signature trailer without claiming signature generation or authenticity.</summary>
public sealed class NdsDownloadPlaySignature
{
    /// <summary>Defines the four-byte identifier, 128-byte signature, and four-byte seed stored after meaningful image data.</summary>
    public const int ByteLength = 0x88;

    /// <summary>Retains an independently owned copy after the fixed trailer structure has been checked.</summary>
    private NdsDownloadPlaySignature(byte[] bytes) => RawData = bytes;

    /// <summary>Preserves the complete stored identifier, signature, and seed in their original byte order.</summary>
    public ReadOnlyMemory<byte> RawData { get; }

    /// <summary>Exposes the opaque 128-byte RSA field separately from late-DS and DSi header signatures.</summary>
    public ReadOnlyMemory<byte> Signature => RawData.Slice(4, 128);

    /// <summary>Decodes the trailing little-endian value without assigning it a timestamp or checksum meaning.</summary>
    public uint Seed => NdsBinary.ReadUInt32(RawData.Span, 0x84);

    /// <summary>Copies a complete previously stored trailer; structural acceptance is not cryptographic verification.</summary>
    /// <param name="bytes">Exactly 0x88 bytes beginning with the Download Play identifier.</param>
    /// <returns>An immutable detached representation suitable for explicit preservation in another build.</returns>
    /// <exception cref="InvalidDataException">The fixed length or identifier is invalid.</exception>
    public static NdsDownloadPlaySignature Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ByteLength || !HasIdentifier(bytes))
        {
            throw new InvalidDataException("A Download Play signature trailer must be exactly 0x88 bytes beginning with 61 63 01 00.");
        }

        return new(bytes.ToArray());
    }

    /// <summary>Recognizes only the exact four-byte identifier, never an embedded substring or a partial prefix.</summary>
    internal static bool HasIdentifier(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4 && bytes[0] == 0x61 && bytes[1] == 0x63 && bytes[2] == 1 && bytes[3] == 0;
}
