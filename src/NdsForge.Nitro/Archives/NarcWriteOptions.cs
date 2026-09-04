using NdsForge.Nitro.Containers;

namespace NdsForge.Nitro.Archives;

/// <summary>Controls deterministic NARC reconstruction without changing file IDs or FNT naming.</summary>
public sealed class NarcWriteOptions
{
    /// <summary>Gets or initializes an optional replacement for the parsed header marker.</summary>
    public NitroByteOrder? HeaderByteOrder { get; init; }

    /// <summary>Gets or initializes the positive payload alignment used after each file.</summary>
    public int FileAlignment { get; init; } = 4;

    /// <summary>Gets or initializes the byte written between aligned file payloads.</summary>
    public byte PaddingByte { get; init; }

    /// <summary>Gets or initializes whether unchanged layout, padding, and trailing bytes should be retained when possible.</summary>
    public bool PreserveSourceLayout { get; init; } = true;

    /// <summary>Rejects alignments that are nonpositive or cannot be handled without overflow.</summary>
    internal void Validate()
    {
        if (FileAlignment <= 0 || FileAlignment > 0x100000)
        {
            throw new ArgumentOutOfRangeException(nameof(FileAlignment));
        }
    }
}
