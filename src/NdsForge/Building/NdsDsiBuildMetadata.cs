namespace NdsForge;

/// <summary>
/// Supplies DSi-only execution, service, storage, and policy metadata while retaining unmodeled extension bytes
/// from an optional template. Physical program, digest, banner, and total-size fields remain layout-owned.
/// </summary>
public sealed class NdsDsiBuildMetadata
{
    /// <summary>Holds bytes <c>0x180</c>-<c>0xFFF</c> so structural imports do not discard reserved metadata.</summary>
    private byte[] _extensionTemplate = new byte[0xE80];
    /// <summary>Stores the 48-byte MBK/WRAM block independently from caller buffers and template mutations.</summary>
    private byte[] _memoryBankSettings = CreateDefaultMemoryBankSettings();
    /// <summary>Stores all sixteen authority-specific rating slots; <c>0x80</c> is the conventional unrated value.</summary>
    private byte[] _ageRatings = Enumerable.Repeat((byte)0x80, 0x10).ToArray();

    /// <summary>Preserves common-header DSi behavior flags separately from the application flags in the extension.</summary>
    public byte DsiFlags { get; set; } = 0x01;

    /// <summary>Defines the DSi territories in which system software may expose the title; all bits set is homebrew-friendly.</summary>
    public uint RegionFlags { get; set; } = uint.MaxValue;

    /// <summary>Controls service and hardware capabilities requested from the DSi execution environment.</summary>
    public uint AccessControl { get; set; }

    /// <summary>Limits which SCFG_EXT hardware-configuration bits the application may change at runtime.</summary>
    public uint ScfgExtMask { get; set; }

    /// <summary>Preserves the application-policy byte at extended-header offset <c>0x1BF</c>.</summary>
    public byte ApplicationFlags { get; set; }

    /// <summary>Points ARM7i services to their runtime device-list structure rather than to a cartridge region.</summary>
    public uint Arm7DeviceListAddress { get; set; }

    /// <summary>Identifies the title to DSi services and save storage; it is independent from the four-byte game code.</summary>
    public ulong TitleId { get; set; }

    /// <summary>Requests public writable save bytes without allocating those bytes inside the ROM image.</summary>
    public uint PublicSaveSize { get; set; }

    /// <summary>Requests private writable save bytes without allocating those bytes inside the ROM image.</summary>
    public uint PrivateSaveSize { get; set; }

    /// <summary>Declares the first modcrypt-transformed image interval, or an empty region when modcrypt is unused.</summary>
    public NdsRegion ModcryptArea1 { get; set; }

    /// <summary>Declares the second modcrypt-transformed image interval, or an empty region when modcrypt is unused.</summary>
    public NdsRegion ModcryptArea2 { get; set; }

    /// <summary>Determines whether authentication fields are cleared or computed under an explicitly named key policy.</summary>
    public NdsDsiIntegrityOptions Integrity { get; set; } = NdsDsiIntegrityOptions.Unauthenticated;

    /// <summary>
    /// Copies all 0xE80 extension bytes from a parsed header, then initializes typed properties from the same
    /// source. Later layout and integrity fields are deliberately regenerated rather than blindly preserved.
    /// </summary>
    /// <param name="header">Parsed extension whose source image may be disposed after this method returns.</param>
    /// <returns>A detached metadata recipe retaining reserved bytes but using unsigned integrity by default.</returns>
    public static NdsDsiBuildMetadata FromHeader(NdsDsiHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        var metadata = new NdsDsiBuildMetadata
        {
            _extensionTemplate = header.RawData.ToArray(),
            _memoryBankSettings = header.MemoryBankSettings.ToArray(),
            _ageRatings = header.AgeRatings.ToArray(),
            RegionFlags = header.RegionFlags,
            AccessControl = header.AccessControl,
            ScfgExtMask = header.ScfgExtMask,
            ApplicationFlags = header.ApplicationFlags,
            Arm7DeviceListAddress = header.Arm7DeviceListAddress,
            TitleId = header.TitleId,
            PublicSaveSize = header.PublicSaveSize,
            PrivateSaveSize = header.PrivateSaveSize,
            ModcryptArea1 = header.ModcryptArea1,
            ModcryptArea2 = header.ModcryptArea2,
        };
        return metadata;
    }

    /// <summary>Copies the exact MBK and WRAM register image stored at header offsets <c>0x180</c>-<c>0x1AF</c>.</summary>
    /// <param name="settings">Exactly 48 bytes in native extended-header order.</param>
    /// <returns>The same metadata object for fluent recipe configuration.</returns>
    public NdsDsiBuildMetadata SetMemoryBankSettings(ReadOnlySpan<byte> settings)
    {
        if (settings.Length != 0x30)
        {
            throw new ArgumentException("DSi memory-bank settings must contain exactly 48 bytes.", nameof(settings));
        }

        _memoryBankSettings = settings.ToArray();
        return this;
    }

    /// <summary>Copies the sixteen raw authority slots without guessing territory-specific flag interpretation.</summary>
    /// <param name="ratings">Exactly sixteen bytes corresponding to header offsets <c>0x2F0</c>-<c>0x2FF</c>.</param>
    /// <returns>The same metadata object for fluent recipe configuration.</returns>
    public NdsDsiBuildMetadata SetAgeRatings(ReadOnlySpan<byte> ratings)
    {
        if (ratings.Length != 0x10)
        {
            throw new ArgumentException("DSi age ratings must contain exactly sixteen bytes.", nameof(ratings));
        }

        _ageRatings = ratings.ToArray();
        return this;
    }

    /// <summary>Returns the detached extension template to the serializer without exposing writable storage publicly.</summary>
    internal ReadOnlyMemory<byte> ExtensionTemplate => _extensionTemplate;

    /// <summary>Returns copied native MBK bytes to the serializer without permitting external mutation.</summary>
    internal ReadOnlyMemory<byte> MemoryBankSettings => _memoryBankSettings;

    /// <summary>Returns copied raw rating slots to the serializer without interpreting their authority-dependent bits.</summary>
    internal ReadOnlyMemory<byte> AgeRatings => _ageRatings;

    /// <summary>Builds the conventional ndstool homebrew MBK register image used when no source template is supplied.</summary>
    /// <returns>Exactly 48 bytes covering global, ARM9, ARM7, and WRAM control registers.</returns>
    private static byte[] CreateDefaultMemoryBankSettings() =>
    [
        0x81, 0x85, 0x89, 0x8D, 0x80, 0x84, 0x88, 0x8C, 0x90, 0x94, 0x98, 0x9C,
        0x80, 0x84, 0x88, 0x8C, 0x90, 0x94, 0x98, 0x9C,
        0x00, 0x00, 0x00, 0x00, 0x40, 0x37, 0xC0, 0x07, 0x00, 0x37, 0x40, 0x07,
        0x00, 0x30, 0x40, 0x00, 0x40, 0x37, 0xC0, 0x07, 0x00, 0x37, 0x40, 0x07,
        0x0F, 0x00, 0x00, 0x03,
    ];
}
