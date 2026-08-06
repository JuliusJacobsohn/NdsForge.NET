using System.Buffers.Binary;
using System.Text;

namespace NdsForge;

/// <summary>Builds deterministic static Nintendo DS banner versions 1 through 3.</summary>
public sealed class NdsBannerBuilder
{
    /// <summary>Stores only explicitly supplied titles so unused fixed-width slots remain deterministically zero-filled.</summary>
    private readonly Dictionary<NdsBannerLanguage, string> _titles = [];
    /// <summary>Uses ergonomic row-major pixels until build time converts them to the console's 8-by-8 tile order.</summary>
    private byte[] _paletteIndices = new byte[32 * 32];
    /// <summary>Preserves the caller's 16 raw BGR555 values, with palette index zero treated as transparent when rendered.</summary>
    private ushort[] _palette = new ushort[16];

    /// <summary>Selects a fixed static layout whose language slots and CRC coverage expand from version one through three.</summary>
    /// <param name="version">Version 1, 2, or 3.</param>
    public NdsBannerBuilder(ushort version = 1)
    {
        if (version is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Static banner version must be 1, 2, or 3.");
        }

        Version = version;
    }

    /// <summary>Controls output length, title-slot count, and how many cumulative CRC fields are populated.</summary>
    public ushort Version { get; }

    /// <summary>Assigns one version-supported UTF-16LE title while reserving a terminating NUL code unit.</summary>
    /// <param name="language">A language supported by the selected version.</param>
    /// <param name="title">The localized title.</param>
    /// <returns>This builder.</returns>
    public NdsBannerBuilder SetTitle(NdsBannerLanguage language, string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        if ((int)language >= GetLanguageCount() ||
            title.Length > 127 ||
            title.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("The title language or length is not supported by this banner version.", nameof(title));
        }

        _titles[language] = title;
        return this;
    }

    /// <summary>Accepts developer-friendly row-major pixels and converts them to tiled 4-bpp banner storage during build.</summary>
    /// <param name="paletteIndices">Exactly 1024 palette indices from zero through 15.</param>
    /// <param name="bgr555Palette">Exactly 16 BGR555 colors.</param>
    /// <returns>This builder.</returns>
    public NdsBannerBuilder SetIndexedIcon(
        ReadOnlySpan<byte> paletteIndices,
        ReadOnlySpan<ushort> bgr555Palette)
    {
        if (paletteIndices.Length != 32 * 32 || bgr555Palette.Length != 16)
        {
            throw new ArgumentException("A banner icon requires 1024 indices and 16 palette colors.");
        }

        if (paletteIndices.ContainsAnyExceptInRange((byte)0, (byte)15))
        {
            throw new ArgumentException("Banner palette indices must be between zero and 15.", nameof(paletteIndices));
        }

        _paletteIndices = paletteIndices.ToArray();
        _palette = bgr555Palette.ToArray();
        return this;
    }

    /// <summary>Builds a checksummed immutable banner.</summary>
    /// <returns>The completed banner.</returns>
    public NdsBanner Build()
    {
        byte[] data = new byte[NdsBanner.GetSize(Version)];
        NdsBinary.WriteUInt16(data, 0, Version);
        WriteIcon(data);
        foreach ((NdsBannerLanguage language, string title) in _titles)
        {
            int offset = 0x240 + ((int)language * 0x100);
            Encoding.Unicode.GetBytes(title, data.AsSpan(offset, 0xFE));
        }

        for (int slot = 0; slot < Version; slot++)
        {
            (int offset, int length) = NdsBanner.GetCrcRegion(slot);
            BinaryPrimitives.WriteUInt16LittleEndian(
                data.AsSpan(2 + (slot * 2)),
                NdsChecksums.ComputeCrc16(data.AsSpan(offset, length)));
        }

        return NdsBanner.Parse(data);
    }

    /// <summary>Packs two four-bit pixels per byte in 8-by-8 tile order and writes the adjacent BGR555 palette.</summary>
    /// <param name="data">Complete zero-initialized banner buffer containing icon regions at offsets <c>0x20</c> and <c>0x220</c>.</param>
    private void WriteIcon(Span<byte> data)
    {
        Span<byte> tiles = data.Slice(0x20, 0x200);
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x += 2)
            {
                int tileOffset = ((((y / 8) * 4) + (x / 8)) * 32) + ((y % 8) * 4) + ((x % 8) / 2);
                byte low = _paletteIndices[(y * 32) + x];
                byte high = _paletteIndices[(y * 32) + x + 1];
                tiles[tileOffset] = (byte)(low | (high << 4));
            }
        }

        for (int index = 0; index < _palette.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data[(0x220 + (index * 2))..], _palette[index]);
        }
    }

    /// <summary>Maps static layout revisions to their historical six-, seven-, or eight-title capacity.</summary>
    /// <returns>The exclusive upper bound for <see cref="NdsBannerLanguage"/> values accepted by <see cref="SetTitle"/>.</returns>
    private int GetLanguageCount() => Version switch
    {
        1 => 6,
        2 => 7,
        3 => 8,
        _ => 0,
    };
}
