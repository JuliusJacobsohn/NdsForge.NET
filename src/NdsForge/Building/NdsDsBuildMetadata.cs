namespace NdsForge;

/// <summary>Retains late-DS extension bytes and component-relative SDK pointers for deliberate structural rebuilds.</summary>
public sealed class NdsDsBuildMetadata
{
    /// <summary>Preserves the complete late-DS extension, including opaque bytes and original authentication fields.</summary>
    private byte[] _extensionTemplate = new byte[0xE80];

    /// <summary>Preserves the common-header DSi behavior byte without selecting a DSi image kind.</summary>
    public byte DsiFlags { get; set; }

    /// <summary>Preserves all eight program-feature bits, including those unrelated to authentication.</summary>
    public NdsProgramFeatures ProgramFeatures { get; set; }

    /// <summary>Locates the ARM9 parameter prefix within its program; null writes an absent pointer.</summary>
    public uint? Arm9ParametersRelativeOffset { get; set; }

    /// <summary>Locates the ARM7 parameter prefix within its program; null writes an absent pointer.</summary>
    public uint? Arm7ParametersRelativeOffset { get; set; }

    /// <summary>Requires an explicit policy whenever authentication is declared; imports do not silently choose one.</summary>
    public NdsDsIntegrityOptions? Integrity { get; set; }

    /// <summary>Copies extension bytes 0x180 through 0xFFF, retaining reserved fields and stored authentication.</summary>
    /// <param name="data">Exactly 0xE80 bytes; the typed program-feature property is refreshed from this template.</param>
    /// <returns>This metadata object.</returns>
    public NdsDsBuildMetadata SetExtensionTemplate(ReadOnlySpan<byte> data)
    {
        if (data.Length != 0xE80)
        {
            throw new ArgumentException("A late-DS extension template must contain exactly 0xE80 bytes.", nameof(data));
        }

        _extensionTemplate = data.ToArray();
        ProgramFeatures = (NdsProgramFeatures)data[0x3F];
        return this;
    }

    /// <summary>Returns an independent copy suitable for an external portable recipe.</summary>
    /// <returns>The complete retained extension template, before typed edits and authentication policy are applied.</returns>
    public byte[] ExportExtensionTemplate() => (byte[])_extensionTemplate.Clone();

    /// <summary>Imports a late-DS extension and anchors both absolute pointers to their original programs.</summary>
    /// <param name="image">Original-DS image with a parsed late-generation extension.</param>
    /// <returns>Detached metadata requiring the caller to select an authentication write policy.</returns>
    public static NdsDsBuildMetadata FromImage(NdsImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        NdsDsExtendedHeader extension = image.Header.DsExtended ??
            throw new ArgumentException("The source does not contain a late-DS header extension.", nameof(image));
        var result = new NdsDsBuildMetadata
        {
            DsiFlags = image.Header.DsiFlags,
            Arm9ParametersRelativeOffset = Anchor(extension.Arm9ParametersOffset, image.Header.Arm9.Data),
            Arm7ParametersRelativeOffset = Anchor(extension.Arm7ParametersOffset, image.Header.Arm7.Data),
        };
        return result.SetExtensionTemplate(image.Header.RawData.Span[0x180..0x1000]);
    }

    /// <summary>Exposes retained extension bytes only to the final header writer.</summary>
    internal ReadOnlyMemory<byte> ExtensionTemplate => _extensionTemplate;

    /// <summary>Checks policies and pointer intervals against final stored program sizes before output mutation.</summary>
    internal void Validate(NdsImageBuilder builder, NdsImageBuildContent? content = null)
    {
        if (builder.Kind != NdsImageKind.NintendoDs || ((int)ProgramFeatures & ~0xFF) != 0)
        {
            throw new InvalidDataException("Late-DS metadata requires original-DS unit mode and byte-sized feature flags.");
        }

        if ((ProgramFeatures & (NdsProgramFeatures.AuthenticatesPrograms | NdsProgramFeatures.AuthenticatesBanner)) != 0 && Integrity is null)
        {
            throw new InvalidDataException("Late-DS authentication requires an explicit preserve, clear, or regenerate policy.");
        }

        Integrity?.Validate(ProgramFeatures, builder.Banner is not null);
        RequirePointer(Arm9ParametersRelativeOffset, content is null
            ? builder.Arm9!.Contents.Length : content.Arm9DeclaredLength - content.Arm9PrefixLength);
        RequirePointer(Arm7ParametersRelativeOffset, content?.Arm7DeclaredLength ?? builder.Arm7!.Contents.Length);
    }

    /// <summary>Retains a complete bounded parameter prefix without requiring its opaque bytes to match a known SDK version.</summary>
    private static uint? Anchor(uint pointer, NdsRegion program)
    {
        if (pointer == 0) { return null; }
        if (pointer < program.Offset || pointer > program.End - 0x24)
        {
            throw new InvalidDataException("A late-DS SDK parameter pointer cannot be anchored inside its declared program.");
        }

        return checked((uint)(pointer - program.Offset));
    }

    /// <summary>Rejects pointers whose complete fixed prefix would not survive program replacement or recompression.</summary>
    private static void RequirePointer(uint? offset, int length)
    {
        if (offset is uint value && (length < 0x24 || value > length - 0x24))
        {
            throw new InvalidDataException("A late-DS SDK parameter prefix extends beyond its rebuilt program.");
        }
    }
}
