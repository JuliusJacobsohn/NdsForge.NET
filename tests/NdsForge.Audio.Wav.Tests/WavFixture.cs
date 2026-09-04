using System.Buffers.Binary;
using System.Text;

namespace NdsForge.Audio.Wav.Tests;

internal static class WavFixture
{
    internal static short[] Samples(int bits, int channels, int frames)
    {
        short[] samples = new short[channels * frames];
        for (int frame = 0; frame < frames; frame++)
        {
            for (int channel = 0; channel < channels; channel++)
            {
                samples[frame * channels + channel] = bits == 8
                    ? (short)((((17 * frame + 67 * channel) & 255) - 128) * 256)
                    : (short)(((997 * frame + 7919 * channel) % 60001) - 30000);
            }
        }
        return samples;
    }

    internal static byte[] PcmBytes(ReadOnlySpan<short> samples)
    {
        byte[] bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++) { BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), samples[i]); }
        return bytes;
    }

    internal static byte[] Format(int channels = 1, int bits = 16, int frames = 8)
        => WavFile.Create(Samples(bits, channels, frames), channels, 22050,
            new() { Encoding = bits == 8 ? WavPcmEncoding.Unsigned8 : WavPcmEncoding.Signed16 }).Chunks[0].Data.ToArray();

    internal static byte[] Envelope(params (string Name, byte[] Data)[] chunks)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("RIFF"u8); writer.Write(0); writer.Write("WAVE"u8);
        foreach (var (name, data) in chunks)
        {
            writer.Write(Encoding.Latin1.GetBytes(name)); writer.Write(data.Length); writer.Write(data);
            if (data.Length % 2 != 0) { writer.Write((byte)0xA7); }
        }
        writer.Seek(4, SeekOrigin.Begin); writer.Write((int)stream.Length - 8);
        return stream.ToArray();
    }
}
