namespace NdsForge.Graphics.Animations;

/// <summary>Describes one NANR frame reference and its exact descriptor metadata.</summary>
public sealed class NanrFrame
{
    internal NanrFrame(uint dataOffset, ushort duration, ushort descriptorFlags, ushort cellIndex)
    {
        DataOffset = dataOffset;
        Duration = duration;
        DescriptorFlags = descriptorFlags;
        CellIndex = cellIndex;
    }

    /// <summary>Gets the offset into the ABNK frame-data area.</summary>
    public uint DataOffset { get; }

    /// <summary>Gets the frame duration field.</summary>
    public ushort Duration { get; }

    /// <summary>Gets the exact trailing frame-descriptor word.</summary>
    public ushort DescriptorFlags { get; }

    /// <summary>Gets the referenced NCER cell index.</summary>
    public ushort CellIndex { get; }
}
