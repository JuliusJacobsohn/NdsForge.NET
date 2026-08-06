using System.Collections.ObjectModel;

namespace NdsForge;

/// <summary>Returns a builder-ready executable plus any Overlay definitions decoded from the same ELF image.</summary>
public sealed class NdsElfImportResult
{
    /// <summary>Freezes imported definitions only after every selected segment and Overlay has passed validation.</summary>
    /// <param name="program">Contiguous main executable definition.</param>
    /// <param name="overlays">Private Overlay definitions in toolchain table order.</param>
    /// <param name="hasOverlaySegments">Whether the ELF advertised Overlay headers, including when import was disabled.</param>
    /// <param name="arm7WramAddress">First selected DSi ARM7 WRAM virtual address, when one was observed.</param>
    internal NdsElfImportResult(
        NdsProgramDefinition program,
        NdsOverlayDefinition[] overlays,
        bool hasOverlaySegments,
        uint? arm7WramAddress)
    {
        Program = program;
        Overlays = new ReadOnlyCollection<NdsOverlayDefinition>(overlays);
        HasOverlaySegments = hasOverlaySegments;
        Arm7WramAddress = arm7WramAddress;
    }

    /// <summary>Contains cartridge-ready bytes with physical-address gaps initialized to zero and a translated entrypoint.</summary>
    public NdsProgramDefinition Program { get; }

    /// <summary>Contains validated private Overlay records and payloads, or an empty collection when none were requested.</summary>
    public IReadOnlyList<NdsOverlayDefinition> Overlays { get; }

    /// <summary>Reports flagged Overlay segments even when <see cref="NdsElfImportOptions.ImportOverlays"/> was disabled.</summary>
    public bool HasOverlaySegments { get; }

    /// <summary>Identifies the first selected DSi ARM7 WRAM virtual address in <c>0x03000000..0x037F7FFF</c>.</summary>
    public uint? Arm7WramAddress { get; }

    /// <summary>Applies the imported Program and every Overlay to a build recipe without hiding recipe mutation.</summary>
    /// <param name="builder">Recipe whose matching processor slot and Overlay collection receive the result.</param>
    /// <returns>The same builder for fluent configuration of unrelated image components.</returns>
    public NdsImageBuilder ApplyTo(NdsImageBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        switch (Program.Processor)
        {
            case NdsProcessor.Arm9: builder.Arm9 = Program; break;
            case NdsProcessor.Arm7: builder.Arm7 = Program; break;
            case NdsProcessor.Arm9i: builder.Arm9i = Program; break;
            case NdsProcessor.Arm7i: builder.Arm7i = Program; break;
            default: throw new InvalidOperationException("The imported Program has an unsupported processor identity.");
        }

        foreach (NdsOverlayDefinition overlay in Overlays)
        {
            builder.AddOverlay(overlay);
        }

        return builder;
    }
}
