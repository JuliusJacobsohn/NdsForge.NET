namespace NdsForge;

/// <summary>Decodes the four-byte Nintendo SDK version stored as major, minor, and sixteen-bit build components.</summary>
public readonly record struct NdsSdkVersion
{
    /// <summary>Creates independently addressable SDK version components.</summary>
    /// <param name="major">Major SDK generation.</param>
    /// <param name="minor">Minor SDK generation.</param>
    /// <param name="build">SDK build number.</param>
    public NdsSdkVersion(byte major, byte minor, ushort build)
    {
        Major = major;
        Minor = minor;
        Build = build;
    }

    /// <summary>Distinguishes SDK generations with incompatible runtime conventions.</summary>
    public byte Major { get; }

    /// <summary>Tracks the feature revision within one compatible SDK generation.</summary>
    public byte Minor { get; }

    /// <summary>Identifies the exact toolchain release within the major and minor revision.</summary>
    public ushort Build { get; }

    /// <summary>Creates a typed version from the header's little-endian packed integer.</summary>
    /// <param name="value">Packed value whose most significant bytes hold major and minor.</param>
    /// <returns>The separated SDK components.</returns>
    public static NdsSdkVersion FromPacked(uint value) =>
        new((byte)(value >> 24), (byte)(value >> 16), (ushort)value);

    /// <summary>Reconstructs the exact packed integer retained by program metadata.</summary>
    public uint PackedValue => ((uint)Major << 24) | ((uint)Minor << 16) | Build;

    /// <summary>Formats the conventional major.minor.build representation.</summary>
    /// <returns>A culture-independent dotted SDK version.</returns>
    public override string ToString() => $"{Major}.{Minor}.{Build}";
}
