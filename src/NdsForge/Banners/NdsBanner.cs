using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;

namespace NdsForge;

/// <summary>Represents a versioned menu icon, localized titles, and optional DSi animation.</summary>
public sealed class NdsBanner
{
    private const int StaticTilesOffset = 0x20;
    private const int StaticPaletteOffset = 0x220;
    private const int TitlesOffset = 0x240;
    private const int AnimatedTilesOffset = 0x1240;
    private const int AnimatedPalettesOffset = 0x2240;
    private const int AnimationSequenceOffset = 0x2340;
    private readonly ReadOnlyMemory<byte> _data;

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

    /// <summary>Gets the raw banner version value.</summary>
    public ushort Version { get; }

    /// <summary>Gets the complete unmodified banner bytes.</summary>
    public ReadOnlyMemory<byte> RawData => _data;

    /// <summary>Gets stored version CRCs in slot order.</summary>
    public IReadOnlyList<ushort> StoredCrcs { get; }

    /// <summary>Gets localized titles supported by this banner version.</summary>
    public IReadOnlyDictionary<NdsBannerLanguage, string> Titles { get; }

    /// <summary>Gets whether this banner contains DSi animated-icon data.</summary>
    public bool IsAnimated => Version == 0x0103;

    /// <summary>Gets the number of localized title slots defined by this version.</summary>
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

    /// <summary>Gets the 64 raw DSi animation sequence values.</summary>
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

    internal static int GetSize(ushort version) => version switch
    {
        1 => 0x840,
        2 => 0x940,
        3 => 0xA40,
        0x0103 => 0x23C0,
        _ => throw new InvalidDataException($"Unsupported banner version 0x{version:X4}."),
    };

    internal static (int Offset, int Length) GetCrcRegion(int slot) => slot switch
    {
        0 => (0x20, 0x820),
        1 => (0x20, 0x920),
        2 => (0x20, 0xA20),
        3 => (0x1240, 0x1180),
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

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

    private static byte ExpandFiveBits(int value) => (byte)((value << 3) | (value >> 2));
}
