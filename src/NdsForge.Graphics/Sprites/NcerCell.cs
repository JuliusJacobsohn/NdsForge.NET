namespace NdsForge.Graphics.Sprites;

/// <summary>Models one NCER cell and its ordered OAM objects.</summary>
public sealed class NcerCell
{
    internal NcerCell(ushort attributes, NcerCellBounds? bounds, IReadOnlyList<NitroObjectEntry> objects, uint? extendedAttribute)
    {
        Attributes = attributes;
        Bounds = bounds;
        Objects = Array.AsReadOnly(objects.ToArray());
        ExtendedAttribute = extendedAttribute;
    }

    /// <summary>Gets the exact cell attribute word.</summary>
    public ushort Attributes { get; }
    /// <summary>Gets whether the attribute word describes a square cell.</summary>
    public bool IsSquare => (Attributes & 0x0800) == 0;
    /// <summary>Gets the attribute-declared square dimension.</summary>
    public int DeclaredSquareDimension => (Attributes & 0x003F) * 8;
    /// <summary>Gets an explicit boundary when the bank stores boundaries.</summary>
    public NcerCellBounds? Bounds { get; }
    /// <summary>Gets objects in serialized OAM order.</summary>
    public IReadOnlyList<NitroObjectEntry> Objects { get; }
    /// <summary>Gets the optional UACT value associated with this cell.</summary>
    public uint? ExtendedAttribute { get; }
}
