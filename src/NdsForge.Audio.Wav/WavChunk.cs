using System.Text;

namespace NdsForge.Audio.Wav;

/// <summary>Describes one stored direct RIFF chunk without discarding unknown identifiers or payloads.</summary>
public readonly record struct WavChunk
{
    /// <summary>Retains a slice of validated source storage, excluding its separate alignment byte.</summary>
    internal WavChunk(uint id, int offset, ReadOnlyMemory<byte> data, byte? paddingByte)
    {
        Identifier = id; Offset = offset; Data = data; PaddingByte = paddingByte;
    }
    /// <summary>Gets the raw little-endian FOURCC value.</summary>
    public uint Identifier { get; }
    /// <summary>Gets the four identifier bytes represented losslessly as Latin-1 characters.</summary>
    public string Name => Encoding.Latin1.GetString([(byte)Identifier, (byte)(Identifier >> 8), (byte)(Identifier >> 16), (byte)(Identifier >> 24)]);
    /// <summary>Gets the absolute offset of the chunk's eight-byte header.</summary>
    public int Offset { get; }
    /// <summary>Gets exactly the declared chunk payload, excluding alignment padding.</summary>
    public ReadOnlyMemory<byte> Data { get; }
    /// <summary>Gets the stored alignment byte when one is present, otherwise null.</summary>
    public byte? PaddingByte { get; }
}
