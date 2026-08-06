using System.Buffers.Binary;
using System.Text;

namespace NdsForge;

/// <summary>Builds deterministic DS banners, including the DSi animated-icon layout.</summary>
public sealed class NdsBannerBuilder
{
    /// <summary>Stores only explicitly supplied titles so unused fixed-width slots remain deterministically zero-filled.</summary>
    private readonly Dictionary<NdsBannerLanguage, string> _titles = [];
    /// <summary>Uses ergonomic row-major pixels until build time converts them to the console's 8-by-8 tile order.</summary>
    private byte[] _paletteIndices = new byte[32 * 32];
    /// <summary>Preserves the caller's 16 raw BGR555 values, with palette index zero treated as transparent when rendered.</summary>
    private ushort[] _palette = new ushort[16];
    /// <summary>Keeps eight independently addressable DSi tile frames in row-major form until serialization.</summary>
    private readonly byte[][] _animatedPaletteIndices = Enumerable.Range(0, 8)
        .Select(static _ => new byte[32 * 32])
        .ToArray();
    /// <summary>Keeps eight DSi palettes separate because sequence entries may pair them with any tile frame.</summary>
    private readonly ushort[][] _animatedPalettes = Enumerable.Range(0, 8)
        .Select(static _ => new ushort[16])
        .ToArray();
    /// <summary>Stores meaningful poses only; build adds zero terminators and unused capacity deterministically.</summary>
    private NdsBannerAnimationStep[] _animationSteps = [];

    /// <summary>Selects a fixed layout whose language slots and CRC coverage expand through the DSi revision.</summary>
    /// <param name="version">Static version 1, 2, or 3, or animated DSi version <c>0x0103</c>.</param>
    public NdsBannerBuilder(ushort version = 1)
    {
        if (version is not (1 or 2 or 3 or 0x0103))
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Banner version must be 1, 2, 3, or 0x0103.");
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
        ValidateIcon(paletteIndices, bgr555Palette);
        _paletteIndices = paletteIndices.ToArray();
        _palette = bgr555Palette.ToArray();
        return this;
    }

    /// <summary>
    /// Assigns one of the eight DSi tile-and-palette slots from conventional row-major indexed pixels. The two slot
    /// numbers in an animation step may later differ, enabling palette cycling without redundant tile frames.
    /// </summary>
    /// <param name="frame">Destination slot from zero through seven.</param>
    /// <param name="paletteIndices">Exactly 1024 palette indices from zero through 15.</param>
    /// <param name="bgr555Palette">Exactly 16 raw BGR555 colors; index zero renders transparently.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidOperationException">The selected banner version has no DSi animation area.</exception>
    public NdsBannerBuilder SetAnimatedFrame(
        int frame,
        ReadOnlySpan<byte> paletteIndices,
        ReadOnlySpan<ushort> bgr555Palette)
    {
        EnsureAnimated();
        ArgumentOutOfRangeException.ThrowIfNegative(frame);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frame, 7);
        ValidateIcon(paletteIndices, bgr555Palette);
        _animatedPaletteIndices[frame] = paletteIndices.ToArray();
        _animatedPalettes[frame] = bgr555Palette.ToArray();
        return this;
    }

    /// <summary>
    /// Replaces the ordered DSi playback sequence. At most 63 poses are accepted so the serialized 64-word table
    /// always contains an explicit zero terminator instead of depending on bytes beyond the structure.
    /// </summary>
    /// <param name="steps">Poses in playback order; an empty sequence leaves the console using the static icon.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidOperationException">The selected banner version has no DSi animation area.</exception>
    public NdsBannerBuilder SetAnimationSequence(IEnumerable<NdsBannerAnimationStep> steps)
    {
        EnsureAnimated();
        ArgumentNullException.ThrowIfNull(steps);
        NdsBannerAnimationStep[] materialized = steps.ToArray();
        if (materialized.Length > 63)
        {
            throw new ArgumentException("A DSi animation accepts at most 63 poses plus its required terminator.", nameof(steps));
        }

        foreach (NdsBannerAnimationStep step in materialized)
        {
            step.Validate();
        }

        _animationSteps = materialized;
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

        if (Version == 0x0103)
        {
            WriteAnimation(data);
        }

        int crcCount = Version == 0x0103 ? 4 : Version;
        for (int slot = 0; slot < crcCount; slot++)
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
        WriteTiles(data.Slice(0x20, 0x200), _paletteIndices);
        WritePalette(data.Slice(0x220, 0x20), _palette);
    }

    /// <summary>Serializes all DSi frame slots and the terminated sequence into their fixed extended-banner regions.</summary>
    /// <param name="data">Complete version <c>0x0103</c> banner buffer.</param>
    private void WriteAnimation(Span<byte> data)
    {
        for (int frame = 0; frame < 8; frame++)
        {
            WriteTiles(data.Slice(0x1240 + (frame * 0x200), 0x200), _animatedPaletteIndices[frame]);
            WritePalette(data.Slice(0x2240 + (frame * 0x20), 0x20), _animatedPalettes[frame]);
        }

        for (int index = 0; index < _animationSteps.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data[(0x2340 + (index * 2))..], _animationSteps[index].Pack());
        }
    }

    /// <summary>Converts one conventional 32-by-32 frame into the console's tiled four-bit storage order.</summary>
    /// <param name="tiles">Exactly 512 destination bytes.</param>
    /// <param name="paletteIndices">Exactly 1024 validated row-major indices.</param>
    private static void WriteTiles(Span<byte> tiles, ReadOnlySpan<byte> paletteIndices)
    {
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x += 2)
            {
                int tileOffset = ((((y / 8) * 4) + (x / 8)) * 32) + ((y % 8) * 4) + ((x % 8) / 2);
                byte low = paletteIndices[(y * 32) + x];
                byte high = paletteIndices[(y * 32) + x + 1];
                tiles[tileOffset] = (byte)(low | (high << 4));
            }
        }
    }

    /// <summary>Writes one sixteen-color BGR555 palette in the little-endian representation consumed by DS hardware.</summary>
    /// <param name="destination">Exactly 32 bytes adjacent to its tile frame.</param>
    /// <param name="palette">Exactly 16 raw color values.</param>
    private static void WritePalette(Span<byte> destination, ReadOnlySpan<ushort> palette)
    {
        for (int index = 0; index < palette.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination[(index * 2)..], palette[index]);
        }
    }

    /// <summary>Applies shared icon-shape and palette-index constraints before retaining caller data.</summary>
    /// <param name="paletteIndices">Candidate row-major pixels.</param>
    /// <param name="bgr555Palette">Candidate raw palette.</param>
    private static void ValidateIcon(ReadOnlySpan<byte> paletteIndices, ReadOnlySpan<ushort> bgr555Palette)
    {
        if (paletteIndices.Length != 32 * 32 || bgr555Palette.Length != 16)
        {
            throw new ArgumentException("A banner icon requires 1024 indices and 16 palette colors.");
        }

        if (paletteIndices.ContainsAnyExceptInRange((byte)0, (byte)15))
        {
            throw new ArgumentException("Banner palette indices must be between zero and 15.", nameof(paletteIndices));
        }
    }

    /// <summary>Prevents animation-only calls from silently discarding data in legacy banner layouts.</summary>
    private void EnsureAnimated()
    {
        if (Version != 0x0103)
        {
            throw new InvalidOperationException("Animated frames require DSi banner version 0x0103.");
        }
    }

    /// <summary>Maps static layout revisions to their historical six-, seven-, or eight-title capacity.</summary>
    /// <returns>The exclusive upper bound for <see cref="NdsBannerLanguage"/> values accepted by <see cref="SetTitle"/>.</returns>
    private int GetLanguageCount() => Version switch
    {
        1 => 6,
        2 => 7,
        3 or 0x0103 => 8,
        _ => 0,
    };
}
