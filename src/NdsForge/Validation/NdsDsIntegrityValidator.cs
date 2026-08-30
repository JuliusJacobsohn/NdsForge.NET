using System.Security.Cryptography;

namespace NdsForge;

/// <summary>Separates late-DS content mismatch, unavailable credentials, and unsupported authentication layouts.</summary>
internal static class NdsDsIntegrityValidator
{
    /// <summary>Checks requested late-DS fields without substituting DSi or ARM9-embedded credentials.</summary>
    internal static void Validate(NdsImage image, List<NdsDiagnostic> diagnostics, NdsValidationOptions options)
    {
        if (!options.RequiresDsAuthentication || image.Header.DsExtended is not NdsDsExtendedHeader extension)
        {
            return;
        }

        if (extension.HasBannerAuthentication)
        {
            ValidateBanner(image, extension, diagnostics, options);
        }

        if (!extension.HasProgramAuthentication)
        {
            return;
        }

        if (options.DsProgramHmacKey.IsEmpty)
        {
            diagnostics.Add(new("NDS1511", NdsDiagnosticSeverity.Warning,
                "Late-DS program and aggregate HMACs were not verified because no program/overlay key was supplied."));
        }
        else
        {
            ValidatePrograms(image, extension, diagnostics, options);
        }

        ValidateOverlays(image, extension, diagnostics, options.DsProgramHmacKey.Span);
        if (options.DsRsaPublicKey is null)
        {
            diagnostics.Add(new("NDS1530", NdsDiagnosticSeverity.Warning,
                "The late-DS header signature was not verified because no trusted RSA public key was supplied.", new(0xF80, 128)));
        }
        else if (!extension.VerifyRsaSignature(options.DsRsaPublicKey))
        {
            diagnostics.Add(new("NDS1531", NdsDiagnosticSeverity.Error,
                "The late-DS header signature does not match the caller-trusted RSA public key.", new(0xF80, 128)));
        }
    }

    /// <summary>Checks exact banner bytes independently from program and signature credentials.</summary>
    private static void ValidateBanner(
        NdsImage image, NdsDsExtendedHeader extension, List<NdsDiagnostic> diagnostics, NdsValidationOptions options)
    {
        if (image.Banner is null)
        {
            diagnostics.Add(new("NDS1500", NdsDiagnosticSeverity.Error,
                "The late-DS header declares banner authentication but no complete banner is available."));
        }
        else if (options.DsBannerHmacKey.IsEmpty)
        {
            diagnostics.Add(new("NDS1501", NdsDiagnosticSeverity.Warning,
                "The late-DS banner HMAC was not verified because no separate banner key was supplied.", new(0x33C, 20)));
        }
        else
        {
            Compare(diagnostics, "NDS1502", "banner", new(0x33C, 20), extension.BannerHmac.Span,
                NdsDsAuthentication.ComputeBannerHmac(image.Banner, options.DsBannerHmacKey.Span));
        }
    }

    /// <summary>Normalizes the secure area only when caller KEY1 material establishes its representation.</summary>
    private static void ValidatePrograms(
        NdsImage image, NdsDsExtendedHeader extension, List<NdsDiagnostic> diagnostics, NdsValidationOptions options)
    {
        try
        {
            byte[] computed = NdsDsProgramAuthentication.Compute(image, options.DsProgramHmacKey.Span, options.SecureAreaKeyTable);
            Compare(diagnostics, "NDS1512", "program", new(0x378, 20), extension.ProgramsHmac.Span, computed);
        }
        catch (NotSupportedException exception)
        {
            diagnostics.Add(new("NDS1510", NdsDiagnosticSeverity.Warning,
                $"The late-DS program HMAC was not verified: {exception.Message}", new(0x378, 20)));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(new("NDS1514", NdsDiagnosticSeverity.Error, exception.Message, new(0x378, 20)));
        }
    }

    /// <summary>Validates coverage independently of credentials and treats an absent aggregate as a separate layout case.</summary>
    private static void ValidateOverlays(
        NdsImage image, NdsDsExtendedHeader extension, List<NdsDiagnostic> diagnostics, ReadOnlySpan<byte> key)
    {
        if (image.Arm9Overlays.Count == 0)
        {
            if (extension.Arm9OverlaysHmac.Span.ContainsAnyExcept((byte)0))
            {
                diagnostics.Add(new("NDS1523", NdsDiagnosticSeverity.Error,
                    "The late-DS aggregate must be absent when there are no ARM9 overlays.", new(0x38C, 20)));
            }

            return;
        }

        try
        {
            _ = NdsDsAuthentication.GetOverlayHashRegions(image);
            if (!key.IsEmpty)
            {
                Compare(diagnostics, "NDS1522", "overlay aggregate", new(0x38C, 20), extension.Arm9OverlaysHmac.Span,
                    NdsDsAuthentication.ComputeOverlayHmac(image, key));
            }
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(new("NDS1520", NdsDiagnosticSeverity.Warning,
                $"The late-DS overlay aggregate was not verified: {exception.Message}", new(0x38C, 20)));
        }
    }

    /// <summary>Reports a mismatch without logging key material or interpreting an HMAC match as publisher identity.</summary>
    private static void Compare(
        List<NdsDiagnostic> diagnostics, string code, string component, NdsRegion field,
        ReadOnlySpan<byte> stored, ReadOnlySpan<byte> computed)
    {
        if (!CryptographicOperations.FixedTimeEquals(stored, computed))
        {
            diagnostics.Add(new(code, NdsDiagnosticSeverity.Error,
                $"The late-DS {component} HMAC does not match the supplied key and covered bytes.", field));
        }
    }
}
