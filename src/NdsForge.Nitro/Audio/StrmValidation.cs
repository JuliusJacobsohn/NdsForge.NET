using System.Buffers.Binary;

namespace NdsForge.Nitro.Audio;

/// <summary>Validates native stream declarations without allocating proportional to untrusted counts.</summary>
internal static class StrmValidation
{
    /// <summary>Checks all traversed ranges and returns the observed state-header length convention.</summary>
    internal static bool Validate(ReadOnlySpan<byte> data, StrmReadOptions options)
    {
        if (data.Length > options.MaximumInputBytes) { throw new InvalidDataException("The STRM input limit was exceeded."); }
        if (data.Length < 104 || !data[..4].SequenceEqual("STRM"u8)) { throw new InvalidDataException("The STRM header is missing or truncated."); }
        if (U16(data, 4) is not (0xFEFF or 0xFFFE) || U16(data, 6) != 0x0100 || U16(data, 14) != 2)
        {
            throw new InvalidDataException("The STRM marker, version, or file block count is unsupported.");
        }
        long declared = U32(data, 8);
        int header = U16(data, 12);
        if (declared > data.Length || header < 16 || header + 88L > declared) { throw new InvalidDataException("The STRM declared size or header length is invalid."); }
        long headLength = U32(data, header + 4);
        if (!data.Slice(header, 4).SequenceEqual("HEAD"u8) || headLength < 80 || header + headLength + 8 > declared)
        {
            throw new InvalidDataException("The STRM HEAD block is missing or out of bounds.");
        }
        int dataHeader = (int)(header + headLength);
        long dataLength = U32(data, dataHeader + 4), dataOffset = U32(data, header + 24);
        if (!data.Slice(dataHeader, 4).SequenceEqual("DATA"u8) || dataLength < 8 || dataHeader + dataLength > declared ||
            dataOffset < dataHeader + 8L || dataOffset > dataHeader + dataLength)
        {
            throw new InvalidDataException("The STRM DATA block or sample pointer is out of bounds.");
        }
        NitroWaveEncoding encoding = (NitroWaveEncoding)data[header + 8];
        int channels = data[header + 10];
        long samples = U32(data, header + 20), loop = U32(data, header + 16);
        if (!Enum.IsDefined(encoding) || channels is not (1 or 2) || U16(data, header + 12) == 0)
        {
            throw new InvalidDataException("The stream encoding, channel count, or sample rate is unsupported.");
        }
        if (samples * channels > options.MaximumSampleValues) { throw new InvalidDataException("The stream sample-value limit was exceeded."); }
        if (data[header + 9] != 0 && loop >= samples) { throw new InvalidDataException("The active stream loop starts outside its sample duration."); }
        long blocks = U32(data, header + 28), normalBytes = U32(data, header + 32), normalSamples = U32(data, header + 36);
        long finalBytes = U32(data, header + 40), finalSamples = U32(data, header + 44);
        if (blocks < 1 || blocks > options.MaximumBlocksPerChannel) { throw new InvalidDataException("The stream block count is zero or exceeds its limit."); }
        if ((blocks > 1 && normalSamples == 0) || (blocks - 1) * normalSamples + finalSamples != samples)
        {
            throw new InvalidDataException("The stream blocks do not describe the declared sample duration.");
        }
        bool excluded = encoding == NitroWaveEncoding.ImaAdpcm && blocks == 1 && finalSamples > 0 && finalBytes == (finalSamples + 1) / 2;
        long physicalFinalBytes = finalBytes + (excluded ? 4 : 0);
        long stored = ((blocks - 1) * normalBytes + physicalFinalBytes) * channels;
        if (stored > dataHeader + dataLength - dataOffset) { throw new InvalidDataException("The stored stream blocks exceed the DATA payload."); }
        int position = (int)dataOffset;
        for (int block = 0; block < blocks; block++)
        {
            int length = (int)(block == blocks - 1 ? physicalFinalBytes : normalBytes);
            int count = (int)(block == blocks - 1 ? finalSamples : normalSamples);
            for (int channel = 0; channel < channels; channel++)
            {
                ValidateBlock(data.Slice(position, length), encoding, count);
                position += length;
            }
        }
        return excluded;
    }

    /// <summary>Checks sample capacity, complete PCM words, and each ADPCM starting state without decoding.</summary>
    internal static void ValidateBlock(ReadOnlySpan<byte> block, NitroWaveEncoding encoding, int count)
    {
        if (block.IsEmpty && count == 0) { return; }
        if (encoding == NitroWaveEncoding.Pcm16 && block.Length % 2 != 0) { throw new InvalidDataException("A stream PCM16 block ends in a partial sample."); }
        if (encoding == NitroWaveEncoding.ImaAdpcm && (block.Length < 4 || (block[2] & 127) > 88))
        {
            throw new InvalidDataException("A stream ADPCM state header is truncated or has an invalid index.");
        }
        long capacity = encoding switch
        {
            NitroWaveEncoding.Pcm8 => block.Length,
            NitroWaveEncoding.Pcm16 => block.Length / 2,
            _ => (block.Length - 4L) * 2,
        };
        if (count > capacity) { throw new InvalidDataException("A stream block contains fewer samples than declared."); }
    }

    /// <summary>Reads a little-endian field from a previously checked header.</summary>
    private static ushort U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
    /// <summary>Reads a little-endian field from a previously checked header.</summary>
    private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
}
