namespace NdsForge;

/// <summary>Decodes the four-byte Nintendo SDK version stored as major, minor, and sixteen-bit build components.</summary>
/// <param name="Major">Major SDK generation.</param>
/// <param name="Minor">Minor SDK generation.</param>
/// <param name="Build">SDK build number.</param>
public readonly record struct NdsSdkVersion(byte Major, byte Minor, ushort Build)
{
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
