namespace NdsForge.Nitro.Compression;

/// <summary>Identifies the type byte used by common Nintendo BIOS and SDK compression envelopes.</summary>
public enum NitroCompressionType
{
    /// <summary>Indicates that no recognized compression envelope was selected.</summary>
    None = 0,

    /// <summary>Uses the original two-byte LZSS back-reference layout.</summary>
    Lz10 = 0x10,

    /// <summary>Uses variable-length LZSS back-references introduced for DS software.</summary>
    Lz11 = 0x11,

    /// <summary>Encodes two four-bit symbols per decoded byte through a serialized Huffman tree.</summary>
    Huffman4 = 0x24,

    /// <summary>Encodes complete byte symbols through a serialized Huffman tree.</summary>
    Huffman8 = 0x28,

    /// <summary>Alternates literal blocks with repeated-byte runs.</summary>
    RunLength = 0x30,
}
