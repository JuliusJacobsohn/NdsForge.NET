using System.Buffers.Binary;
using System.Numerics;

namespace NdsForge.Audio.Wav;

/// <summary>Validates supported PCM format declarations independently from chunk order.</summary>
internal readonly record struct WavFormat(ushort Tag, int Channels, uint Rate, uint ByteRate, ushort Alignment,
    WavPcmEncoding Encoding, ushort ValidBits, uint ChannelMask)
{
    internal static readonly Guid PcmSubformat = new("00000001-0000-0010-8000-00aa00389b71");

    /// <summary>Checks standard or extensible PCM with complete eight- or sixteen-bit mono/stereo frames.</summary>
    internal static WavFormat Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || data.Length == 17) { throw new InvalidDataException("The WAV format chunk is truncated."); }
        ushort tag = U16(data, 0), channels = U16(data, 2), alignment = U16(data, 12), bits = U16(data, 14);
        uint rate = U32(data, 4), byteRate = U32(data, 8), mask = 0;
        ushort validBits = bits;
        if (tag is not (1 or 0xFFFE)) { throw new NotSupportedException("Only integer PCM WAV formats are supported."); }
        if (channels is not (1 or 2) || bits is not (8 or 16)) { throw new NotSupportedException("Only mono/stereo eight- and sixteen-bit PCM WAV samples are supported."); }
        if (rate == 0 || alignment != channels * bits / 8 || rate * (long)alignment != byteRate)
        {
            throw new InvalidDataException("The WAV sample rate, frame alignment, or byte rate is inconsistent.");
        }
        if (tag == 0xFFFE)
        {
            if (data.Length < 40 || U16(data, 16) < 22 || U16(data, 16) + 18 > data.Length) { throw new InvalidDataException("The extensible PCM header is truncated."); }
            if (new Guid(data.Slice(24, 16)) != PcmSubformat) { throw new NotSupportedException("The extensible WAV subformat is not integer PCM."); }
            validBits = U16(data, 18); mask = U32(data, 20);
            if (validBits == 0 || validBits > bits) { throw new InvalidDataException("The extensible valid-bit count is invalid."); }
            if (validBits != bits) { throw new NotSupportedException("Partial-precision PCM containers are not supported."); }
            ValidateMask(mask, channels);
        }
        return new(tag, channels, rate, byteRate, alignment,
            bits == 8 ? WavPcmEncoding.Unsigned8 : WavPcmEncoding.Signed16, validBits, mask);
    }

    /// <summary>Allows an unspecified mask or exactly one assigned speaker bit per stored channel.</summary>
    internal static void ValidateMask(uint mask, int channels)
    {
        if (mask != 0 && BitOperations.PopCount(mask) != channels) { throw new InvalidDataException("The WAV channel mask does not match the channel count."); }
    }
    /// <summary>Reads a bounded format field.</summary>
    private static ushort U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
    /// <summary>Reads a bounded format field.</summary>
    private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
}
