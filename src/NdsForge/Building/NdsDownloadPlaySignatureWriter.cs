namespace NdsForge;

/// <summary>Retains opaque Download Play trailers after layout changes and reports that their signatures were not regenerated.</summary>
internal static class NdsDownloadPlaySignatureWriter
{
    /// <summary>Rejects a partial recognized signature rather than silently dropping it during a semantic write.</summary>
    internal static void ValidateSource(NdsImage image)
    {
        if (image.HasTruncatedDownloadPlaySignature)
        {
            throw new InvalidDataException("A truncated Download Play signature trailer cannot be preserved by a semantic write.");
        }
    }

    /// <summary>Writes the exact retained trailer at the finalized meaningful-image boundary, excluding it from that boundary.</summary>
    internal static async ValueTask WriteAsync(
        Stream destination, NdsDownloadPlaySignature? signature, long usedSize, CancellationToken cancellationToken)
    {
        if (signature is null) { return; }
        destination.Position = usedSize;
        await destination.WriteAsync(signature.RawData, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Augments ordinary write findings without presenting retained opaque signature bytes as verified authentication.</summary>
    internal static IReadOnlyList<NdsDiagnostic> AppendDiagnostic(
        IReadOnlyList<NdsDiagnostic> diagnostics, NdsDownloadPlaySignature? signature, long usedSize)
    {
        if (signature is null) { return diagnostics; }
        return diagnostics.Append(new NdsDiagnostic("NDS1550", NdsDiagnosticSeverity.Warning,
            "The Download Play trailer was preserved without cryptographic verification or regeneration; changes to its signed header or programs may make it stale.",
            new(usedSize, NdsDownloadPlaySignature.ByteLength))).ToArray();
    }
}
