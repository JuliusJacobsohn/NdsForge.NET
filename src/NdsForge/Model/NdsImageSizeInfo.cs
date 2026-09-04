namespace NdsForge;

/// <summary>Separates declared content, capacity metadata, physical storage, and unclassified trailing material.</summary>
public sealed class NdsImageSizeInfo
{
    /// <summary>Captures immutable size evidence without reading or classifying payload bytes as padding.</summary>
    internal NdsImageSizeInfo(NdsImage image)
    {
        PhysicalSize = image.Length;
        CommonUsedSize = image.Header.UsedImageSize;
        DsiUsedSize = image.Header.Dsi?.TotalImageSize;
        DeviceCapacityExponent = image.Header.DeviceCapacityExponent;
        DeviceCapacityBytes = DecodeDeviceCapacity(DeviceCapacityExponent);
        var diagnostics = new List<NdsDiagnostic>();
        long declaredEnd = Math.Max(image.Header.RawData.Length, image.Header.HeaderSize);
        foreach (NdsRegion region in NdsCarrierLayoutParser.GetComponents(image.Header, image.FileSystem, image.Banner, image.DownloadPlaySignature))
        {
            declaredEnd = Math.Max(declaredEnd, region.End);
        }
        if (image.CarrierLayout is NdsCartridgeLayout cartridge && cartridge.TwlRegionStart != 0 && image.Header.Dsi is not null)
        {
            declaredEnd = Math.Max(declaredEnd, cartridge.TwlRegionStart + 0x7000);
        }
        if (image.Header.Dsi is { } dsi)
        {
            foreach (NdsRegion region in new[] { dsi.NtrDigest, dsi.TwlDigest, dsi.ModcryptArea1, dsi.ModcryptArea2 })
            {
                declaredEnd = Math.Max(declaredEnd, region.End);
            }
            if (image.Header.BannerOffset != 0) { declaredEnd = Math.Max(declaredEnd, (long)image.Header.BannerOffset + dsi.BannerSize); }
        }
        if (image.Header.DsExtended is not null && image.Header.ProgramFeatures.HasFlag(NdsProgramFeatures.AuthenticatesPrograms))
        {
            try
            {
                foreach (NdsRegion region in NdsDsAuthentication.GetOverlayHashRegions(image)) { declaredEnd = Math.Max(declaredEnd, region.End); }
            }
            catch (InvalidDataException)
            {
                declaredEnd = Math.Max(declaredEnd, PhysicalSize);
                diagnostics.Add(new("NDS1573", NdsDiagnosticSeverity.Warning,
                    "Late-DS authenticated coverage cannot be established; the declared extent conservatively retains the full physical image."));
            }
        }
        DeclaredContentEnd = Math.Max(declaredEnd, Math.Max(CommonUsedSize, DsiUsedSize ?? 0));
        if (DeviceCapacityBytes is null)
        {
            diagnostics.Add(new("NDS1570", NdsDiagnosticSeverity.Error,
                "The stored device-capacity exponent cannot be represented as a positive 64-bit byte length.", new(0x14, 1)));
        }
        if (DeclaredContentEnd > PhysicalSize)
        {
            diagnostics.Add(new("NDS1571", NdsDiagnosticSeverity.Error,
                "The declared content extent exceeds the physical image; resizing cannot repair missing content."));
        }
        if (image.HasTruncatedDownloadPlaySignature)
        {
            diagnostics.Add(new("NDS1572", NdsDiagnosticSeverity.Error,
                "A recognized Download Play trailer is truncated; its missing bytes cannot be classified as padding."));
        }
        Diagnostics = diagnostics.AsReadOnly();
    }

    /// <summary>Gets the actual byte length, independently of every header declaration.</summary>
    public long PhysicalSize { get; }

    /// <summary>Gets the common header's NTR used-size field, which excludes DSi-only data.</summary>
    public uint CommonUsedSize { get; }

    /// <summary>Gets the DSi total-used-size field, including zero when unspecified, or null without a DSi extension.</summary>
    public uint? DsiUsedSize { get; }

    /// <summary>Preserves the raw capacity byte even when its mathematical interpretation is unrepresentable.</summary>
    public byte DeviceCapacityExponent { get; }

    /// <summary>Gets 128 KiB multiplied by two to the stored exponent, or null instead of overflow or masked shifts.</summary>
    public long? DeviceCapacityBytes { get; }

    /// <summary>Gets the exclusive end required by declared components, protocol windows, used-size fields, and recognized trailers.</summary>
    public long DeclaredContentEnd { get; }

    /// <summary>Locates all physical bytes after the common used field; these can include DSi programs and signatures, not just padding.</summary>
    public NdsRegion? PostUsedData => CommonUsedSize < PhysicalSize ? new(CommonUsedSize, PhysicalSize - CommonUsedSize) : null;

    /// <summary>Locates bytes beyond declared content without assuming they are padding or safe to discard.</summary>
    public NdsRegion? TrailingData => DeclaredContentEnd < PhysicalSize ? new(DeclaredContentEnd, PhysicalSize - DeclaredContentEnd) : null;

    /// <summary>Reports unrepresentable capacity or incomplete declared content independently of cryptographic validation.</summary>
    public IReadOnlyList<NdsDiagnostic> Diagnostics { get; }

    /// <summary>Bounds the shift before evaluation so malformed exponent bytes cannot wrap around the machine word.</summary>
    internal static long? DecodeDeviceCapacity(byte exponent) => exponent <= 45 ? 0x20000L << exponent : null;
}
