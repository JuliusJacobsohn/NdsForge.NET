namespace NdsForge;

/// <summary>Reports a detached legacy ARM7 hook image and the regions introduced by the compatibility transform.</summary>
public sealed class NdsLegacyArm7HookResult
{
    /// <summary>Captures completed output only after header patching and whole-image CRC preservation are verified.</summary>
    /// <param name="image">Independent transformed image bytes.</param>
    /// <param name="relocatedArm7">Combined original ARM7, hook, and header-backup load region.</param>
    /// <param name="hook">Appended trainer payload within the relocated ARM7 region.</param>
    /// <param name="headerBackup">Unmodified original 512-byte common header loaded after the hook.</param>
    /// <param name="crc32">Standard CRC32 of the completed output, useful when checking legacy patch workflows.</param>
    internal NdsLegacyArm7HookResult(
        byte[] image,
        NdsRegion relocatedArm7,
        NdsRegion hook,
        NdsRegion headerBackup,
        uint crc32)
    {
        Image = image;
        RelocatedArm7 = relocatedArm7;
        Hook = hook;
        HeaderBackup = headerBackup;
        Crc32 = crc32;
    }

    /// <summary>Contains every transformed image byte plus the four-byte CRC correction word beyond declared used size.</summary>
    public ReadOnlyMemory<byte> Image { get; }
    /// <summary>Locates the header-declared ARM7 region after relocation and expansion.</summary>
    public NdsRegion RelocatedArm7 { get; }
    /// <summary>Locates the aligned caller hook executed through the patched ARM7 entrypoint.</summary>
    public NdsRegion Hook { get; }
    /// <summary>Locates the original common header appended for hook code that needs to restore boot metadata.</summary>
    public NdsRegion HeaderBackup { get; }
    /// <summary>Records standard CRC32 of the completed physical output, including bytes beyond declared used size.</summary>
    public uint Crc32 { get; }
}
