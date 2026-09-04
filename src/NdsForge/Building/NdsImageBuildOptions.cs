namespace NdsForge;

/// <summary>Controls deterministic physical placement without mixing Layout policy into image metadata.</summary>
public sealed record NdsImageBuildOptions
{
    /// <summary>Uses a 16 KiB header area, 512-byte component boundaries, four-byte file boundaries, and verification.</summary>
    public static NdsImageBuildOptions Default { get; } = new();

    /// <summary>Selects NdsForge defaults or a precisely versioned external-tool Layout contract.</summary>
    public NdsImageBuildProfile Profile { get; init; }

    /// <summary>Reserves bytes before the first Program; DS homebrew conventionally uses <c>0x4000</c>.</summary>
    public int HeaderSize { get; init; } = 0x4000;

    /// <summary>Aligns Programs, filesystem tables, and Banner starts; it must be a positive power of two.</summary>
    public int SectionAlignment { get; init; } = 0x200;

    /// <summary>Aligns individual FAT payload starts; four bytes avoids unnecessary growth while retaining word alignment.</summary>
    public int FileAlignment { get; init; } = 4;

    /// <summary>Fills every Layout gap after the reserved header area, making otherwise unspecified bytes reproducible.</summary>
    public byte PaddingByte { get; init; } = 0xFF;

    /// <summary>Requests a cartridge capacity of exactly 128 KiB times a power of two, up to 4 GiB; null selects the smallest capacity containing the layout.</summary>
    /// <remarks>This changes the header capacity, not the physical length, unless <see cref="PadToDeviceCapacity"/> is enabled. Digital SRL recipes reject explicit cartridge-capacity requests.</remarks>
    public long? RequestedDeviceCapacityBytes { get; init; }

    /// <summary>Extends cartridge output to its selected capacity using <see cref="PaddingByte"/>, without enlarging common or DSi used-size fields.</summary>
    /// <remarks>False retains the compact structural layout. Digital SRL recipes reject this cartridge-only policy.</remarks>
    public bool PadToDeviceCapacity { get; init; }

    /// <summary>Reopens the completed stream and validates its structure, checksums, and NitroFS payload mapping.</summary>
    public bool VerifyOutput { get; init; } = true;

    /// <summary>Allows a successful path build to atomically replace an existing regular destination file.</summary>
    public bool OverwriteDestination { get; init; }

    /// <summary>Requires power-of-two alignments and a header large enough to keep Programs outside the secure-area boundary.</summary>
    /// <exception cref="ArgumentException">A size or alignment cannot produce a valid deterministic Layout.</exception>
    internal void Validate()
    {
        if (HeaderSize < 0x4000 || !IsPowerOfTwo(SectionAlignment) || !IsPowerOfTwo(FileAlignment))
        {
            throw new ArgumentException(
                "The header must be at least 0x4000 bytes and all alignments must be positive powers of two.");
        }

        if (RequestedDeviceCapacityBytes is long capacity &&
            (capacity < 0x20000 || capacity > 0x100000000L || (capacity & (capacity - 1)) != 0))
        {
            throw new ArgumentException("Requested cartridge capacity must be a power of two from 128 KiB through 4 GiB.");
        }
    }

    /// <summary>Recognizes positive powers of two accepted by the writer's checked bitwise alignment formula.</summary>
    /// <param name="value">Candidate byte alignment.</param>
    /// <returns><see langword="true"/> only for 1, 2, 4, 8, and subsequent powers of two.</returns>
    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
}
