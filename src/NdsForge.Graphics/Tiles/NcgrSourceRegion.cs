namespace NdsForge.Graphics.Tiles;

/// <summary>Stores the optional NCGR CPOS source rectangle in pixels.</summary>
public readonly record struct NcgrSourceRegion
{
    /// <summary>Creates one source rectangle.</summary>
    /// <param name="x">Horizontal origin.</param>
    /// <param name="y">Vertical origin.</param>
    /// <param name="width">Source width.</param>
    /// <param name="height">Source height.</param>
    public NcgrSourceRegion(ushort x, ushort y, ushort width, ushort height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>Gets the horizontal origin.</summary>
    public ushort X { get; }

    /// <summary>Gets the vertical origin.</summary>
    public ushort Y { get; }

    /// <summary>Gets the source width.</summary>
    public ushort Width { get; }

    /// <summary>Gets the source height.</summary>
    public ushort Height { get; }
}
