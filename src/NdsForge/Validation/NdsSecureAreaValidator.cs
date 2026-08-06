namespace NdsForge;

/// <summary>Translates pure secure-area inspection into stable validation diagnostics with explicit key semantics.</summary>
internal static class NdsSecureAreaValidator
{
    /// <summary>Reports structural ambiguity, failed keyed recognition, and encrypted-representation CRC mismatches.</summary>
    /// <param name="image">Read-only source and common header identity.</param>
    /// <param name="diagnostics">Shared ordered finding accumulator.</param>
    /// <param name="keyTable">Optional externally supplied KEY1 schedule.</param>
    public static void Validate(
        NdsImage image,
        List<NdsDiagnostic> diagnostics,
        NdsKey1KeyTable? keyTable)
    {
        NdsSecureAreaInspection inspection;
        try
        {
            inspection = NdsSecureArea.Inspect(image, keyTable);
        }
        catch (ArgumentException exception)
        {
            diagnostics.Add(new(
                "NDS1401",
                NdsDiagnosticSeverity.Error,
                $"The product code cannot initialize KEY1 validation: {exception.Message}",
                new(0x0C, 4)));
            return;
        }

        if (inspection.State == NdsSecureAreaState.Malformed)
        {
            diagnostics.Add(new(
                "NDS1402",
                NdsDiagnosticSeverity.Warning,
                "The header places ARM9 in the secure-area interval, but the physical image does not contain all 16 KiB.",
                inspection.Region));
        }
        else if (inspection.State == NdsSecureAreaState.Unrecognized && keyTable is not null)
        {
            diagnostics.Add(new(
                "NDS1403",
                NdsDiagnosticSeverity.Error,
                "The supplied KEY1 table and product code do not recover a recognized secure-area identifier.",
                inspection.Region));
        }

        if (inspection.IsCrcValid == false)
        {
            diagnostics.Add(new(
                "NDS1404",
                NdsDiagnosticSeverity.Error,
                $"The secure-area CRC stores 0x{inspection.StoredCrc:X4}, but the encrypted representation calculates 0x{inspection.CalculatedCrc:X4}.",
                inspection.Region));
        }
    }
}
