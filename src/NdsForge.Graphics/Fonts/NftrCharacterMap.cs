namespace NdsForge.Graphics.Fonts;

/// <summary>Models one linked NFTR CMAP block.</summary>
public sealed class NftrCharacterMap
{
    internal NftrCharacterMap(ushort firstCharacter, ushort lastCharacter, NftrCharacterMapMethod method,
        IReadOnlyList<NftrCharacterMapping> mappings)
    {
        FirstCharacter = firstCharacter;
        LastCharacter = lastCharacter;
        Method = method;
        Mappings = Array.AsReadOnly(mappings.ToArray());
    }

    /// <summary>Gets the inclusive character-range start.</summary>
    public ushort FirstCharacter { get; }

    /// <summary>Gets the inclusive character-range end.</summary>
    public ushort LastCharacter { get; }

    /// <summary>Gets the block's serialized mapping method.</summary>
    public NftrCharacterMapMethod Method { get; }

    /// <summary>Gets mapped character/glyph pairs in serialized or derived order.</summary>
    public IReadOnlyList<NftrCharacterMapping> Mappings { get; }
}
