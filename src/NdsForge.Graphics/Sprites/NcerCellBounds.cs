namespace NdsForge.Graphics.Sprites;

/// <summary>Stores an explicit NCER cell bounding rectangle.</summary>
public readonly record struct NcerCellBounds
{
    /// <summary>Creates one rectangle from its signed edges.</summary>
    /// <param name="minimumX">Minimum X edge.</param>
    /// <param name="minimumY">Minimum Y edge.</param>
    /// <param name="maximumX">Maximum X edge.</param>
    /// <param name="maximumY">Maximum Y edge.</param>
    public NcerCellBounds(short minimumX, short minimumY, short maximumX, short maximumY)
    {
        MinimumX = minimumX;
        MinimumY = minimumY;
        MaximumX = maximumX;
        MaximumY = maximumY;
    }

    /// <summary>Gets the minimum X edge.</summary>
    public short MinimumX { get; }
    /// <summary>Gets the minimum Y edge.</summary>
    public short MinimumY { get; }
    /// <summary>Gets the maximum X edge.</summary>
    public short MaximumX { get; }
    /// <summary>Gets the maximum Y edge.</summary>
    public short MaximumY { get; }
    /// <summary>Gets the signed width.</summary>
    public int Width => MaximumX - MinimumX;
    /// <summary>Gets the signed height.</summary>
    public int Height => MaximumY - MinimumY;
}
