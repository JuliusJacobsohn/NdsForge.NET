using System.Buffers.Binary;

namespace NdsForge.Graphics.Fonts;

internal static class NftrFontWriter
{
    public static byte[] Write(NftrFont source, IReadOnlyList<byte[]> pixels, IReadOnlyList<NftrGlyphMetrics> metrics)
    {
        if (pixels.Count == 0 || pixels.Count > ushort.MaxValue + 1)
            throw new InvalidOperationException("A canonical NFTR requires between 1 and 65,536 glyphs.");
        byte[] cglp = WriteCglp(source, pixels);
        byte[] cwdh = WriteCwdh(metrics);
        byte[][] maps = source.CharacterMaps.Select(WriteCmap).ToArray();
        int finfOffset = 0x10;
        int finfLength = source.ExtendedMetrics is null ? 0x1C : 0x20;
        int cglpOffset = finfOffset + finfLength;
        int cwdhOffset = cglpOffset + cglp.Length;
        int firstMapOffset = cwdhOffset + cwdh.Length;
        int total = firstMapOffset + maps.Sum(static map => map.Length);
        byte[] result = new byte[total];
        "RTFN"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), 0xFEFF);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), source.Version);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), 0x10);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14), checked((ushort)(3 + maps.Length)));

        Span<byte> finf = result.AsSpan(finfOffset, finfLength);
        "FNIF"u8.CopyTo(finf);
        BinaryPrimitives.WriteUInt32LittleEndian(finf[4..], (uint)finfLength);
        finf[8] = source.FontType;
        finf[9] = source.LineHeight;
        BinaryPrimitives.WriteUInt16LittleEndian(finf[10..], source.FallbackGlyphIndex);
        NftrFontBuilder.WriteMetrics(finf[12..], source.DefaultMetrics);
        finf[15] = (byte)source.Encoding;
        BinaryPrimitives.WriteUInt32LittleEndian(finf[16..], (uint)(cglpOffset + 8));
        BinaryPrimitives.WriteUInt32LittleEndian(finf[20..], (uint)(cwdhOffset + 8));
        BinaryPrimitives.WriteUInt32LittleEndian(finf[24..], (uint)(firstMapOffset + 8));
        if (source.ExtendedMetrics is NftrExtendedMetrics extended)
        {
            finf[28] = extended.FontHeight;
            finf[29] = extended.FontWidth;
            finf[30] = (byte)extended.BearingY;
            finf[31] = (byte)extended.BearingX;
        }
        cglp.CopyTo(result.AsSpan(cglpOffset));
        cwdh.CopyTo(result.AsSpan(cwdhOffset));
        int cursor = firstMapOffset;
        for (int index = 0; index < maps.Length; index++)
        {
            if (index + 1 < maps.Length)
                BinaryPrimitives.WriteUInt32LittleEndian(maps[index].AsSpan(16), (uint)(cursor + maps[index].Length + 8));
            maps[index].CopyTo(result.AsSpan(cursor));
            cursor += maps[index].Length;
        }
        return result;
    }

    private static byte[] WriteCglp(NftrFont source, IReadOnlyList<byte[]> pixels)
    {
        int length = Align4(checked(0x10 + (pixels.Count * source.GlyphDataLength)));
        byte[] result = new byte[length];
        "PLGC"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)length);
        result[8] = source.CellWidth;
        result[9] = source.CellHeight;
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(10), source.GlyphDataLength);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), source.GlyphFlags);
        result[14] = source.BitsPerPixel;
        result[15] = source.RotationFlags;
        for (int index = 0; index < pixels.Count; index++)
            NftrFontBuilder.EncodePixels(pixels[index], source.BitsPerPixel,
                result.AsSpan(0x10 + (index * source.GlyphDataLength), source.GlyphDataLength));
        return result;
    }

    private static byte[] WriteCwdh(IReadOnlyList<NftrGlyphMetrics> metrics)
    {
        int length = Align4(checked(0x10 + (metrics.Count * 3)));
        byte[] result = new byte[length];
        "HDWC"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(10), checked((ushort)(metrics.Count - 1)));
        for (int index = 0; index < metrics.Count; index++)
            NftrFontBuilder.WriteMetrics(result.AsSpan(0x10 + (index * 3)), metrics[index]);
        return result;
    }

    private static byte[] WriteCmap(NftrCharacterMap map)
    {
        int range = map.LastCharacter - map.FirstCharacter + 1;
        int length = map.Method switch
        {
            NftrCharacterMapMethod.Direct => 0x18,
            NftrCharacterMapMethod.Table => Align4(0x14 + (range * 2)),
            NftrCharacterMapMethod.Scan => checked(0x18 + (map.Mappings.Count * 4)),
            _ => throw new InvalidOperationException("The NFTR CMAP method is unsupported."),
        };
        byte[] result = new byte[length];
        "PAMC"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(8), map.FirstCharacter);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(10), map.LastCharacter);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), (uint)map.Method);
        if (map.Method == NftrCharacterMapMethod.Direct)
        {
            if (map.Mappings.Count != range) throw new InvalidOperationException("A direct NFTR CMAP is incomplete.");
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(20), map.Mappings[0].GlyphIndex);
        }
        else if (map.Method == NftrCharacterMapMethod.Table)
        {
            result.AsSpan(20, range * 2).Fill(0xFF);
            foreach (NftrCharacterMapping mapping in map.Mappings)
            {
                int index = mapping.CharacterCode - map.FirstCharacter;
                BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(20 + (index * 2)), mapping.GlyphIndex);
            }
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(20), checked((ushort)map.Mappings.Count));
            for (int index = 0; index < map.Mappings.Count; index++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(22 + (index * 4)), map.Mappings[index].CharacterCode);
                BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(24 + (index * 4)), map.Mappings[index].GlyphIndex);
            }
        }
        return result;
    }

    private static int Align4(int value) => checked((value + 3) & ~3);
}
