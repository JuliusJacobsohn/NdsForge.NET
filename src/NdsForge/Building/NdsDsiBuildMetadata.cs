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
    /// <summary>Stores six shared-data file-size unit bytes in native slot order.</summary>
    private byte[] _sharedDataFileSizes = new byte[6];
    /// <summary>Tracks a parsed component-relative first modcrypt interval across structural relocation.</summary>
    private NdsProcessor? _modcryptArea1Anchor;
    /// <summary>Tracks a parsed component-relative second modcrypt interval across structural relocation.</summary>
    private NdsProcessor? _modcryptArea2Anchor;
    /// <summary>Retains the first interval's byte displacement from its source Program.</summary>
    private long _modcryptArea1RelativeOffset;
    /// <summary>Retains the second interval's byte displacement from its source Program.</summary>
    private long _modcryptArea2RelativeOffset;
    /// <summary>Stores the caller-visible first interval independently from an optional import anchor.</summary>
    private NdsRegion _modcryptArea1;
    /// <summary>Stores the caller-visible second interval independently from an optional import anchor.</summary>
    private NdsRegion _modcryptArea2;

    /// <summary>Preserves common-header DSi behavior flags separately from the application flags in the extension.</summary>
    public byte DsiFlags { get; set; } = 0x01;

    /// <summary>Gets or sets defined execution and modcrypt bits through the complete raw <see cref="DsiFlags"/> byte.</summary>
    public NdsDsiCryptoPolicy CryptoPolicy
    {
        get => (NdsDsiCryptoPolicy)DsiFlags;
        set => DsiFlags = (byte)((DsiFlags & 0xF0) | ((int)value & 0x0F));
    }

    /// <summary>Defines the DSi territories in which system software may expose the title; all bits set is homebrew-friendly.</summary>
    public uint RegionFlags { get; set; } = uint.MaxValue;

    /// <summary>Gets or sets named territory permissions through the complete raw <see cref="RegionFlags"/> word.</summary>
    public NdsDsiRegionPermissions Regions
    {
        get => (NdsDsiRegionPermissions)RegionFlags;
        set => RegionFlags = (RegionFlags & ~0x3Fu) | ((uint)value & 0x3Fu);
    }

    /// <summary>Controls service and hardware capabilities requested from the DSi execution environment.</summary>
    public uint AccessControl { get; set; }

    /// <summary>Gets or sets named capabilities through the complete raw <see cref="AccessControl"/> word.</summary>
    public NdsDsiAccessCapabilities AccessControlFlags
    {
        get => (NdsDsiAccessCapabilities)(long)AccessControl;
        set => AccessControl = (AccessControl & ~0x8001FFFFu) | ((uint)(long)value & 0x8001FFFFu);
    }

    /// <summary>Limits which SCFG_EXT hardware-configuration bits the application may change at runtime.</summary>
    public uint ScfgExtMask { get; set; }

    /// <summary>Preserves the application-policy byte at extended-header offset <c>0x1BF</c>.</summary>
    public byte ApplicationFlags { get; set; }

    /// <summary>Gets or sets named application capabilities through the complete raw <see cref="ApplicationFlags"/> byte.</summary>
    public NdsDsiApplicationFeatures ApplicationFeatures
    {
        get => (NdsDsiApplicationFeatures)ApplicationFlags;
        set => ApplicationFlags = (byte)value;
    }

    /// <summary>Gets or sets the title's required EULA revision byte.</summary>
    public byte EulaVersion { get; set; } = 1;

    /// <summary>Controls whether system software evaluates the sixteen parental-rating slots.</summary>
    public byte AgeRatingsUsage { get; set; }

    /// <summary>Points ARM7i services to their runtime device-list structure rather than to a cartridge region.</summary>
    public uint Arm7DeviceListAddress { get; set; }

    /// <summary>Identifies the title to DSi services and save storage; it is independent from the four-byte game code.</summary>
    public ulong TitleId { get; set; }

    /// <summary>Requests public writable save bytes without allocating those bytes inside the ROM image.</summary>
    public uint PublicSaveSize { get; set; }

    /// <summary>Requests private writable save bytes without allocating those bytes inside the ROM image.</summary>
    public uint PrivateSaveSize { get; set; }

    /// <summary>Declares the first modcrypt-transformed image interval, or an empty region when modcrypt is unused.</summary>
    public NdsRegion ModcryptArea1
    {
        get => _modcryptArea1;
        set
        {
            _modcryptArea1 = value;
            _modcryptArea1Anchor = null;
            _modcryptArea1RelativeOffset = 0;
        }
    }

    /// <summary>Declares the second modcrypt-transformed image interval, or an empty region when modcrypt is unused.</summary>
    public NdsRegion ModcryptArea2
    {
        get => _modcryptArea2;
        set
        {
            _modcryptArea2 = value;
            _modcryptArea2Anchor = null;
            _modcryptArea2RelativeOffset = 0;
        }
    }

    /// <summary>Determines whether authentication fields are cleared or computed under an explicitly named key policy.</summary>
    public NdsDsiIntegrityOptions Integrity { get; set; } = NdsDsiIntegrityOptions.Unauthenticated;

    /// <summary>
    /// Enables hierarchical content authentication when non-null. Building tables requires the same explicit
    /// HMAC key policy used for component fields; null emits the format's valid all-zero “digests absent” form.
    /// </summary>
    public NdsDsiDigestOptions? Digests { get; set; }

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
            _sharedDataFileSizes = header.SharedDataFileSizes.ToArray(),
            RegionFlags = header.RegionFlags,
            AccessControl = header.AccessControl,
            ScfgExtMask = header.ScfgExtMask,
            ApplicationFlags = header.ApplicationFlags,
            EulaVersion = header.EulaVersion,
            AgeRatingsUsage = header.AgeRatingsUsage,
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

    /// <summary>Copies six shared-data file-size units in their native slot order.</summary>
    /// <param name="sizes">Exactly six unit bytes corresponding to shared-data files zero through five.</param>
    /// <returns>The same metadata object for fluent recipe configuration.</returns>
    public NdsDsiBuildMetadata SetSharedDataFileSizes(ReadOnlySpan<byte> sizes)
    {
        if (sizes.Length != 6)
        {
            throw new ArgumentException("DSi shared-data sizes must contain exactly six bytes.", nameof(sizes));
        }

        _sharedDataFileSizes = sizes.ToArray();
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

    /// <summary>Replaces one authority slot with its exact typed rating byte.</summary>
    /// <param name="rating">Authority index and complete stored byte.</param>
    /// <returns>The same metadata object for fluent recipe configuration.</returns>
    public NdsDsiBuildMetadata SetAgeRating(NdsDsiAgeRating rating)
    {
        if (!Enum.IsDefined(rating.Authority))
        {
            throw new ArgumentOutOfRangeException(nameof(rating), rating.Authority, "Unknown DSi rating authority.");
        }

        _ageRatings[(int)rating.Authority] = rating.RawValue;
        return this;
    }

    /// <summary>Projects copied native MBK bytes without exposing writable builder storage.</summary>
    public NdsDsiMemoryBankConfiguration MemoryBanks => new(_memoryBankSettings);

    /// <summary>Projects all authority slots without exposing writable builder storage.</summary>
    public IReadOnlyList<NdsDsiAgeRating> Ratings => Array.AsReadOnly(Enumerable.Range(0, 16)
        .Select(index => new NdsDsiAgeRating((NdsDsiAgeRatingAuthority)index, _ageRatings[index]))
        .ToArray());

    /// <summary>Returns the detached extension template to the serializer without exposing writable storage publicly.</summary>
    internal ReadOnlyMemory<byte> ExtensionTemplate => _extensionTemplate;

    /// <summary>Returns copied native MBK bytes to the serializer without permitting external mutation.</summary>
    internal ReadOnlyMemory<byte> MemoryBankSettings => _memoryBankSettings;

    /// <summary>Returns copied shared-data size units to the serializer without permitting external mutation.</summary>
    internal ReadOnlyMemory<byte> SharedDataFileSizes => _sharedDataFileSizes;

    /// <summary>Returns copied raw rating slots to the serializer without interpreting their authority-dependent bits.</summary>
    internal ReadOnlyMemory<byte> AgeRatings => _ageRatings;

    /// <summary>Anchors imported absolute modcrypt offsets to Programs so a deterministic rebuild can relocate them safely.</summary>
    /// <param name="programs">Source Programs whose original physical offsets define possible anchors.</param>
    internal void AnchorModcryptAreas(IEnumerable<NdsProgram> programs)
    {
        NdsProgram[] candidates = programs.ToArray();
        (_modcryptArea1Anchor, _modcryptArea1RelativeOffset) = FindAnchor(ModcryptArea1, candidates);
        (_modcryptArea2Anchor, _modcryptArea2RelativeOffset) = FindAnchor(ModcryptArea2, candidates);
    }

    /// <summary>Resolves one imported area against final Program placement while leaving caller-authored absolute areas unchanged.</summary>
    /// <param name="area">Original absolute interval and preserved length.</param>
    /// <param name="first">Selects the first or second area's independently discovered anchor.</param>
    /// <param name="layout">Final build layout supplying relocated Program offsets.</param>
    /// <returns>A final absolute interval coherent with the generated image.</returns>
    internal NdsRegion ResolveModcryptArea(NdsRegion area, bool first, NdsImageBuildLayout layout)
    {
        NdsProcessor? anchor = first ? _modcryptArea1Anchor : _modcryptArea2Anchor;
        long relativeOffset = first ? _modcryptArea1RelativeOffset : _modcryptArea2RelativeOffset;
        if (anchor is null)
        {
            return area;
        }

        NdsRegion program = anchor switch
        {
            NdsProcessor.Arm9 => layout.Arm9,
            NdsProcessor.Arm7 => layout.Arm7,
            NdsProcessor.Arm9i => layout.Arm9i!.Value,
            NdsProcessor.Arm7i => layout.Arm7i!.Value,
            _ => throw new InvalidDataException($"Unsupported modcrypt Program anchor {anchor}."),
        };
        return new(checked(program.Offset + relativeOffset), area.Length);
    }

    /// <summary>Recognizes an interval beginning within a source Program and records only its relocatable displacement.</summary>
    /// <param name="area">Header-declared absolute modcrypt interval.</param>
    /// <param name="programs">Parsed source Programs in any order.</param>
    /// <returns>Processor anchor and relative byte displacement, or no anchor for an empty or external interval.</returns>
    private static (NdsProcessor? Anchor, long RelativeOffset) FindAnchor(
        NdsRegion area,
        IReadOnlyList<NdsProgram> programs)
    {
        if (area.IsEmpty)
        {
            return (null, 0);
        }

        NdsProgram? program = programs.FirstOrDefault(candidate =>
            area.Offset >= candidate.Data.Offset && area.Offset < candidate.Data.End);
        return program is null
            ? (null, 0)
            : (program.Processor, area.Offset - program.Data.Offset);
    }

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
