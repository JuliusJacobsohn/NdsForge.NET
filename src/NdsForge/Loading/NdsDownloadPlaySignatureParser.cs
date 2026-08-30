namespace NdsForge;

/// <summary>Reads at most one fixed trailer at the declared used-image boundary without scanning capacity padding.</summary>
internal static class NdsDownloadPlaySignatureParser
{
    /// <summary>Returns a complete trailer or an explicit recognized-truncation flag after physically bounded reads.</summary>
    internal static (NdsDownloadPlaySignature? Signature, bool Truncated) Parse(IImageDataSource source, NdsHeader header)
    {
        int length = AvailableLength(source.Length, header.UsedImageSize);
        if (length == 0) { return (null, false); }
        byte[] bytes = new byte[length];
        source.ReadExactly(header.UsedImageSize, bytes);
        return Interpret(bytes);
    }

    /// <summary>Uses asynchronous bounded I/O while preserving the same fixed-position recognition rules.</summary>
    internal static async ValueTask<(NdsDownloadPlaySignature? Signature, bool Truncated)> ParseAsync(
        IImageDataSource source, NdsHeader header, CancellationToken cancellationToken)
    {
        int length = AvailableLength(source.Length, header.UsedImageSize);
        if (length == 0) { return (null, false); }
        byte[] bytes = new byte[length];
        await source.ReadExactlyAsync(header.UsedImageSize, bytes, cancellationToken).ConfigureAwait(false);
        return Interpret(bytes);
    }

    /// <summary>Rejects absent, header-internal, beyond-EOF and too-short identifier positions before any source access.</summary>
    private static int AvailableLength(long imageLength, uint usedSize) =>
        usedSize < 0x200 || usedSize > imageLength - 4 ? 0 : checked((int)Math.Min(NdsDownloadPlaySignature.ByteLength, imageLength - usedSize));

    /// <summary>Distinguishes complete stored trailers from recognized identifiers lacking their complete fixed payload.</summary>
    private static (NdsDownloadPlaySignature? Signature, bool Truncated) Interpret(ReadOnlySpan<byte> bytes)
    {
        if (!NdsDownloadPlaySignature.HasIdentifier(bytes)) { return (null, false); }
        return bytes.Length == NdsDownloadPlaySignature.ByteLength
            ? (NdsDownloadPlaySignature.Parse(bytes), false) : (null, true);
    }
}
