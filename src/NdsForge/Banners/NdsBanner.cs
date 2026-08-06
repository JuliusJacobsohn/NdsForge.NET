using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;

namespace NdsForge;

/// <summary>Represents a versioned menu icon, localized titles, and optional DSi animation.</summary>
public sealed class NdsBanner
{
    /// <summary>Begins the common 512-byte, 4-bpp static icon tile block after version and CRC fields.</summary>
    private const int StaticTilesOffset = 0x20;
    /// <summary>Begins the common 16-entry BGR555 palette used by the static icon.</summary>
    private const int StaticPaletteOffset = 0x220;
    /// <summary>Begins fixed 0x100-byte UTF-16LE title slots shared by all banner revisions.</summary>
    private const int TitlesOffset = 0x240;
    /// <summary>Begins eight DSi icon tile frames after the legacy-compatible banner prefix.</summary>
    private const int AnimatedTilesOffset = 0x1240;
    /// <summary>Begins eight DSi palette frames paired independently by animation sequence entries.</summary>
    private const int AnimatedPalettesOffset = 0x2240;
    /// <summary>Begins the 64-entry DSi sequence controlling frame duration, flips, tile frame, and palette frame.</summary>
    private const int AnimationSequenceOffset = 0x2340;
    /// <summary>Retains an immutable byte-for-byte banner copy for CRC validation, extraction, and future lossless fields.</summary>
    private readonly ReadOnlyMemory<byte> _data;

    /// <summary>Decodes trusted, exact-length banner bytes into language-aware metadata without discarding unknown reserved data.</summary>
    /// <param name="data">Private copy already validated against the version's fixed structure size.</param>
    internal NdsBanner(ReadOnlyMemory<byte> data)
    {
        _data = data;
        Version = NdsBinary.ReadUInt16(data.Span, 0);
        StoredCrcs = Enumerable.Range(0, 4)
            .Select(index => NdsBinary.ReadUInt16(data.Span, 2 + (index * 2)))
            .ToArray();

        var titles = new Dictionary<NdsBannerLanguage, string>();
        for (int index = 0; index < LanguageCount; index++)
        {
            ReadOnlySpan<byte> titleBytes = data.Span.Slice(TitlesOffset + (index * 0x100), 0x100);
            int length = FindUtf16Terminator(titleBytes);
            titles.Add((NdsBannerLanguage)index, Encoding.Unicode.GetString(titleBytes[..length]));
        }

        Titles = new ReadOnlyDictionary<NdsBannerLanguage, string>(titles);
    }

    /// <summary>Parses a complete raw banner from memory.</summary>
    /// <param name="data">Exactly one supported banner structure.</param>
    /// <returns>The immutable parsed banner.</returns>
    public static NdsBanner Parse(ReadOnlyMemory<byte> data)
    {
        if (data.Length < 2)
        {
            throw new InvalidDataException("A banner is too small to contain a version.");
        }

        ushort version = NdsBinary.ReadUInt16(data.Span, 0);
        int expected = GetSize(version);
        if (data.Length != expected)
        {
            throw new InvalidDataException(
                $"Banner version 0x{version:X4} requires 0x{expected:X} bytes, not 0x{data.Length:X}.");
        }

        return new(data.ToArray());
    }

    /// <summary>Selects title count and CRC layout: <c>1</c>-<c>3</c> are static, while <c>0x0103</c> adds DSi animation.</summary>
    public ushort Version { get; }

    /// <summary>Preserves reserved fields and animation bits not projected into typed properties, enabling lossless export.</summary>
    public ReadOnlyMemory<byte> RawData => _data;

    /// <summary>Gets stored version CRCs in slot order.</summary>
    public IReadOnlyList<ushort> StoredCrcs { get; }

    /// <summary>Gets localized titles supported by this banner version.</summary>
    public IReadOnlyDictionary<NdsBannerLanguage, string> Titles { get; }

    /// <summary>Gets whether this banner contains DSi animated-icon data.</summary>
    public bool IsAnimated => Version == 0x0103;

    /// <summary>Maps historical revisions to six, seven, or eight fixed 0x100-byte UTF-16LE slots.</summary>
    public int LanguageCount => Version switch
    {
        1 => 6,
        2 => 7,
        3 or 0x0103 => 8,
        _ => 0,
    };

    /// <summary>Renders the static 32-by-32 icon as row-major RGBA32 pixels.</summary>
    /// <returns>A 4096-byte RGBA32 pixel buffer.</returns>
    public byte[] RenderIconRgba32() => Render(
        _data.Span.Slice(StaticTilesOffset, 0x200),
        _data.Span.Slice(StaticPaletteOffset, 0x20));

    /// <summary>Renders one DSi animated-icon tile and palette frame as RGBA32.</summary>
    /// <param name="tileFrame">The tile frame from zero through seven.</param>
    /// <param name="paletteFrame">The palette frame from zero through seven.</param>
    /// <returns>A 4096-byte RGBA32 pixel buffer.</returns>
    public byte[] RenderAnimatedIconRgba32(int tileFrame, int paletteFrame)
    {
        if (!IsAnimated)
        {
            throw new InvalidOperationException("This banner has no animated icon.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(tileFrame);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tileFrame, 7);
        ArgumentOutOfRangeException.ThrowIfNegative(paletteFrame);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(paletteFrame, 7);
        return Render(
            _data.Span.Slice(AnimatedTilesOffset + (tileFrame * 0x200), 0x200),
            _data.Span.Slice(AnimatedPalettesOffset + (paletteFrame * 0x20), 0x20));
    }

    /// <summary>Returns the 64 packed DSi sequence words without interpreting duration, frame, palette, or flip bitfields.</summary>
    /// <returns>An empty array for non-animated banners.</returns>
    public ushort[] GetAnimationSequence()
    {
        if (!IsAnimated)
        {
            return [];
        }

        var sequence = new ushort[64];
        for (int index = 0; index < sequence.Length; index++)
        {
            sequence[index] = NdsBinary.ReadUInt16(_data.Span, AnimationSequenceOffset + (index * 2));
        }

        return sequence;
    }

    /// <summary>Validates every cumulative CRC slot defined by the version and reports its absolute protected region.</summary>
    /// <param name="bannerOffset">Image offset used to translate banner-relative coverage into diagnostics.</param>
    /// <returns>Zero or more stable errors; reserved CRC slots from earlier versions are intentionally ignored.</returns>
    internal IEnumerable<NdsDiagnostic> ValidateCrcs(uint bannerOffset)
    {
        int slots = Version switch
        {
            1 => 1,
            2 => 2,
            3 => 3,
            0x0103 => 4,
            _ => 0,
        };

        for (int slot = 0; slot < slots; slot++)
        {
            (int offset, int length) = GetCrcRegion(slot);
            ushort calculated = NdsChecksums.ComputeCrc16(_data.Span.Slice(offset, length));
            if (calculated != StoredCrcs[slot])
            {
                yield return new(
                    $"NDS130{slot + 1}",
                    NdsDiagnosticSeverity.Error,
                    $"Banner CRC slot {slot} stores 0x{StoredCrcs[slot]:X4}, but the calculated value is 0x{calculated:X4}.",
                    new(bannerOffset + offset, length));
            }
        }
    }

    /// <summary>Maps a supported version word to the only byte length valid for that layout.</summary>
    /// <param name="version">Raw value at banner offset zero.</param>
    /// <returns>Structure length through its final title or DSi sequence field.</returns>
    internal static int GetSize(ushort version) => version switch
    {
        1 => 0x840,
        2 => 0x940,
        3 => 0xA40,
        0x0103 => 0x23C0,
        _ => throw new InvalidDataException($"Unsupported banner version 0x{version:X4}."),
    };

    /// <summary>Defines banner-relative checksum coverage, which grows cumulatively across static revisions.</summary>
    /// <param name="slot">CRC field index zero through three; slot three protects DSi animation data only.</param>
    /// <returns>Offset and byte count excluding the version and stored CRC fields.</returns>
    internal static (int Offset, int Length) GetCrcRegion(int slot) => slot switch
    {
        0 => (0x20, 0x820),
        1 => (0x20, 0x920),
        2 => (0x20, 0xA20),
        3 => (0x1240, 0x1180),
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    /// <summary>Finds a two-byte-aligned NUL so embedded zero bytes within non-ASCII UTF-16 code units do not truncate text.</summary>
    /// <param name="bytes">One complete even-length title slot.</param>
    /// <returns>Byte length preceding the terminator, or the full slot length when none is present.</returns>
    private static int FindUtf16Terminator(ReadOnlySpan<byte> bytes)
    {
        for (int index = 0; index < bytes.Length; index += 2)
        {
            if (bytes[index] == 0 && bytes[index + 1] == 0)
            {
                return index;
            }
        }

        return bytes.Length;
    }

    /// <summary>Converts console-native 8-by-8 tiled 4-bpp pixels and BGR555 colors into conventional row-major RGBA32.</summary>
    /// <param name="tiles">Exactly 512 bytes encoding a 32-by-32 frame.</param>
    /// <param name="palette">Exactly 32 bytes encoding 16 little-endian colors.</param>
    /// <returns>4,096 bytes in red, green, blue, alpha order; palette index zero receives alpha zero.</returns>
    private static byte[] Render(ReadOnlySpan<byte> tiles, ReadOnlySpan<byte> palette)
    {
        var rgba = new byte[32 * 32 * 4];
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                int tileOffset = ((((y / 8) * 4) + (x / 8)) * 32) + ((y % 8) * 4) + ((x % 8) / 2);
                int paletteIndex = (x & 1) == 0 ? tiles[tileOffset] & 0x0F : tiles[tileOffset] >> 4;
                ushort color = BinaryPrimitives.ReadUInt16LittleEndian(palette[(paletteIndex * 2)..]);
                int pixelOffset = ((y * 32) + x) * 4;
                rgba[pixelOffset] = ExpandFiveBits(color & 0x1F);
                rgba[pixelOffset + 1] = ExpandFiveBits((color >> 5) & 0x1F);
                rgba[pixelOffset + 2] = ExpandFiveBits((color >> 10) & 0x1F);
                rgba[pixelOffset + 3] = paletteIndex == 0 ? (byte)0 : byte.MaxValue;
            }
        }

        return rgba;
    }

    /// <summary>Replicates high color bits when scaling BGR555 channels to eight bits, matching common hardware conversion.</summary>
    /// <param name="value">Unsigned five-bit channel value from zero through 31.</param>
    /// <returns>An eight-bit channel spanning the full zero-through-255 range.</returns>
    private static byte ExpandFiveBits(int value) => (byte)((value << 3) | (value >> 2));
}
