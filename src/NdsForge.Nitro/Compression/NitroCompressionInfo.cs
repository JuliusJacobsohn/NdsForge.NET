namespace NdsForge.Nitro.Compression;

/// <summary>Projects the shared type and decoded-size prefix without processing codec-specific payload data.</summary>
public readonly record struct NitroCompressionInfo
{
    /// <summary>Creates an inspected common compression envelope.</summary>
    /// <param name="type">Codec selected by the first byte.</param>
    /// <param name="decodedLength">Exact output bytes declared by the envelope.</param>
    /// <param name="headerLength">Four-byte ordinary header or eight-byte extended-size header.</param>
    public NitroCompressionInfo(NitroCompressionType type, int decodedLength, int headerLength)
    {
        Type = type;
        DecodedLength = decodedLength;
        HeaderLength = headerLength;
    }

    /// <summary>Selects the token/tree grammar used after the common prefix.</summary>
    public NitroCompressionType Type { get; }

    /// <summary>Defines the exact byte count expected from successful decoding.</summary>
    public int DecodedLength { get; }

    /// <summary>Locates codec-specific bytes after the ordinary or extended common prefix.</summary>
    public int HeaderLength { get; }
}
