namespace NdsForge;

/// <summary>Identifies how ARM9 bytes containing decoded-runtime metadata are stored in the image.</summary>
public enum NdsProgramStorageEncoding
{
    /// <summary>The complete program is stored verbatim.</summary>
    Plain,

    /// <summary>A verbatim prefix is followed by a bottom-up LZ-compressed suffix.</summary>
    Blz,
}
