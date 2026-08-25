using System.Security.Cryptography;

namespace NdsForge;

/// <summary>Validates classic-DS per-overlay authentication without bundling or assuming external key material.</summary>
internal static class NdsOverlayAuthenticationValidator
{
    /// <summary>Defines the embedded key-block width established by independently verified cartridge layouts.</summary>
    private const int EmbeddedKeyLength = 64;
    /// <summary>Identifies candidate SDK key blocks; a candidate is trusted only after all declared records match.</summary>
    private static ReadOnlySpan<byte> KeyMarker => [0x21, 0x06, 0xC0, 0xDE];

    /// <summary>Reports structural defects and verifies every flagged record with explicit or proven embedded key bytes.</summary>
    internal static void Validate(
        NdsImage image,
        List<NdsDiagnostic> diagnostics,
        NdsValidationOptions options)
    {
        NdsOverlayAuthenticationTable? table = image.Arm9OverlayAuthentication;
        if (table is null)
        {
            return;
        }

        switch (table.State)
        {
            case NdsOverlayAuthenticationTableState.MissingFooter:
                diagnostics.Add(new(
                    "NDS1210",
                    NdsDiagnosticSeverity.Error,
                    "ARM9 overlays request Download Play authentication, but the program has no recognized SDK footer."));
                return;
            case NdsOverlayAuthenticationTableState.MissingTablePointer:
                diagnostics.Add(new(
                    "NDS1211",
                    NdsDiagnosticSeverity.Error,
                    "ARM9 overlays request Download Play authentication, but the SDK footer has no authentication-table pointer."));
                return;
            case NdsOverlayAuthenticationTableState.TableOutOfRange:
                diagnostics.Add(new(
                    "NDS1212",
                    NdsDiagnosticSeverity.Error,
                    $"The ARM9 Download Play authentication table at decoded offset 0x{table.RelativeOffset:X} exceeds the 0x{table.DecodedProgramLength:X}-byte program."));
                return;
            case NdsOverlayAuthenticationTableState.Complete:
                break;
            default:
                throw new InvalidOperationException($"Unsupported overlay authentication state {table.State}.");
        }

        NdsOverlay[] authenticated = image.Arm9Overlays.Where(static overlay => overlay.IsAuthenticated).ToArray();
        if (authenticated.Length == 0)
        {
            return;
        }

        ReadOnlyMemory<byte> key = options.Arm9OverlayHmacKey;
        byte[] discoveredKey = [];
        if (key.IsEmpty && !TryFindEmbeddedKey(image, table, out discoveredKey, out _))
        {
            diagnostics.Add(new(
                "NDS1213",
                NdsDiagnosticSeverity.Warning,
                "No conventional embedded ARM9 key block validates every Download Play overlay record; the table is stale or uses unsupported key placement."));
            return;
        }

        if (key.IsEmpty)
        {
            key = discoveredKey;
        }

        AddDigestFailures(image, key.Span, diagnostics);
    }

    /// <summary>Discovers key material only by proving a marked 64-byte candidate against every flagged record.</summary>
    internal static bool TryFindEmbeddedKey(
        NdsImage image,
        NdsOverlayAuthenticationTable table,
        out byte[] key,
        out int relativeOffset)
    {
        key = [];
        relativeOffset = -1;
        NdsOverlay? first = image.Arm9Overlays.FirstOrDefault(static overlay => overlay.IsAuthenticated);
        if (first?.AuthenticationRecord is null || first.Data is null)
        {
            return false;
        }

        ReadOnlySpan<byte> program = table.DecodedProgram.Span;
        int cursor = 0;
        while (cursor <= program.Length - EmbeddedKeyLength)
        {
            int marker = program[cursor..].IndexOf(KeyMarker);
            if (marker < 0)
            {
                break;
            }

            int candidateOffset = cursor + marker;
            byte[] candidate = program.Slice(candidateOffset, EmbeddedKeyLength).ToArray();
            byte[] firstDigest = ComputeHmac(image, first, candidate);
            if (firstDigest.AsSpan().SequenceEqual(first.AuthenticationRecord.HmacSha1.Span) &&
                KeyMatchesEveryRecord(image, candidate))
            {
                key = candidate;
                relativeOffset = candidateOffset;
                return true;
            }

            cursor = candidateOffset + 1;
        }

        return false;
    }

    /// <summary>Checks all flagged overlays without publishing one diagnostic per candidate key during discovery.</summary>
    private static bool KeyMatchesEveryRecord(NdsImage image, ReadOnlySpan<byte> key)
    {
        foreach (NdsOverlay overlay in image.Arm9Overlays.Where(static value => value.IsAuthenticated))
        {
            if (overlay.AuthenticationRecord is null || overlay.Data is null ||
                !ComputeHmac(image, overlay, key).AsSpan().SequenceEqual(overlay.AuthenticationRecord.HmacSha1.Span))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Emits stable errors for explicit-key mismatches or payloads that cannot be read.</summary>
    private static void AddDigestFailures(
        NdsImage image,
        ReadOnlySpan<byte> key,
        List<NdsDiagnostic> diagnostics)
    {
        foreach (NdsOverlay overlay in image.Arm9Overlays.Where(static value => value.IsAuthenticated))
        {
            if (overlay.AuthenticationRecord is null || overlay.Data is null)
            {
                diagnostics.Add(new(
                    "NDS1214",
                    NdsDiagnosticSeverity.Error,
                    $"ARM9 overlay {overlay.Id} has no readable Download Play authentication input."));
                continue;
            }

            byte[] calculated = ComputeHmac(image, overlay, key);
            if (!calculated.AsSpan().SequenceEqual(overlay.AuthenticationRecord.HmacSha1.Span))
            {
                diagnostics.Add(new(
                    "NDS1215",
                    NdsDiagnosticSeverity.Error,
                    $"ARM9 overlay {overlay.Id} does not match its stored Download Play HMAC-SHA1 record.",
                    overlay.Data));
            }
        }
    }

    /// <summary>Streams one complete stored overlay allocation through the legacy format-mandated HMAC.</summary>
    private static byte[] ComputeHmac(NdsImage image, NdsOverlay overlay, ReadOnlySpan<byte> key)
    {
#pragma warning disable CA5350 // Classic DS Download Play authentication is defined as HMAC-SHA1.
        using IncrementalHash hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, key);
#pragma warning restore CA5350
        using Stream stream = image.OpenRead(overlay.Data!.Value);
        byte[] buffer = new byte[64 * 1024];
        int read;
        while ((read = stream.Read(buffer)) != 0)
        {
            hash.AppendData(buffer, 0, read);
        }

        return hash.GetHashAndReset();
    }
}
