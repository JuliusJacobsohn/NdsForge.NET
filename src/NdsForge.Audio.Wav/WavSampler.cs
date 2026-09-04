using System.Buffers.Binary;

namespace NdsForge.Audio.Wav;

/// <summary>Provides bounded sampler metadata, loop records, and opaque sampler-specific bytes.</summary>
public sealed class WavSampler
{
    private readonly byte[] _data;

    /// <summary>Retains a complete validated sampler payload and its immutable projections.</summary>
    private WavSampler(byte[] data, WavSamplerMetadata metadata, WavLoop[] loops, int extraOffset, int extraLength)
    {
        _data = data;
        Metadata = metadata;
        Loops = Array.AsReadOnly(loops);
        SamplerData = data.AsMemory(extraOffset, extraLength);
    }

    /// <summary>Gets the stored identification, tuning, and synchronization fields.</summary>
    public WavSamplerMetadata Metadata { get; }
    /// <summary>Gets sampler loops in stored order, including types not representable by native DS formats.</summary>
    public IReadOnlyList<WavLoop> Loops { get; }
    /// <summary>Gets the declared opaque sampler-specific bytes following the loop records.</summary>
    public ReadOnlyMemory<byte> SamplerData { get; }
    /// <summary>Gets the complete source payload, including any uninterpreted trailing extension.</summary>
    public ReadOnlyMemory<byte> RawData => _data;

    /// <summary>Creates detached sampler metadata without changing loop direction, counts, or tuning.</summary>
    /// <param name="loops">Ordered loops with nonnegative starts and strictly later exclusive ends.</param>
    /// <param name="metadata">Optional raw fields; omitted metadata uses a unity note of sixty.</param>
    /// <param name="samplerData">Optional opaque sampler-specific bytes.</param>
    /// <param name="options">Byte and loop limits applied before allocation.</param>
    /// <returns>A sampler chunk payload; the enclosing WAV additionally validates each loop against its duration.</returns>
    public static WavSampler Create(IReadOnlyList<WavLoop> loops, WavSamplerMetadata? metadata = null,
        ReadOnlySpan<byte> samplerData = default, WavReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(loops);
        options ??= new(); options.Validate(); metadata ??= new();
        long length = 36 + loops.Count * 24L + samplerData.Length;
        if (loops.Count > options.MaximumLoops || length > options.MaximumInputBytes) { throw new InvalidDataException("The sampler allocation or loop limit was exceeded."); }
        foreach (WavLoop loop in loops) { ValidateLoop(loop, int.MaxValue); }
        byte[] data = new byte[(int)length];
        uint[] fields = [metadata.Manufacturer, metadata.Product, metadata.SamplePeriodNanoseconds, metadata.MidiUnityNote,
            metadata.MidiPitchFraction, metadata.SmpteFormat, metadata.SmpteOffset, (uint)loops.Count, (uint)samplerData.Length];
        for (int i = 0; i < fields.Length; i++) { Put(data, i * 4, fields[i]); }
        for (int i = 0; i < loops.Count; i++)
        {
            WavLoop loop = loops[i]; int offset = 36 + i * 24;
            Put(data, offset, loop.Identifier); Put(data, offset + 4, loop.Type);
            Put(data, offset + 8, (uint)loop.StartFrame); Put(data, offset + 12, (uint)(loop.EndFrameExclusive - 1));
            Put(data, offset + 16, loop.Fraction); Put(data, offset + 20, loop.PlayCount);
        }
        samplerData.CopyTo(data.AsSpan(36 + loops.Count * 24));
        return Parse(data, options, int.MaxValue);
    }

    /// <summary>Checks counts, loop endpoints, and opaque data ranges before copying the complete payload.</summary>
    internal static WavSampler Parse(ReadOnlySpan<byte> data, WavReadOptions options, int frames)
    {
        if (data.Length < 36 || data.Length > options.MaximumInputBytes) { throw new InvalidDataException("The sampler chunk is truncated or exceeds its limit."); }
        uint count = U32(data, 28), extraLength = U32(data, 32);
        long extraOffset = 36 + count * 24L;
        if (count > options.MaximumLoops || extraOffset + extraLength > data.Length) { throw new InvalidDataException("The sampler loops or opaque data exceed their bounds."); }
        var loops = new WavLoop[(int)count];
        for (int i = 0; i < loops.Length; i++)
        {
            int offset = 36 + i * 24;
            uint start = U32(data, offset + 8); long end = U32(data, offset + 12) + 1L;
            if (start > int.MaxValue || end > int.MaxValue) { throw new InvalidDataException("The sampler loop frame range is not representable."); }
            loops[i] = new(U32(data, offset), U32(data, offset + 4), (int)start, (int)end, U32(data, offset + 16), U32(data, offset + 20));
            ValidateLoop(loops[i], frames);
        }
        var metadata = new WavSamplerMetadata
        {
            Manufacturer = U32(data, 0),
            Product = U32(data, 4),
            SamplePeriodNanoseconds = U32(data, 8),
            MidiUnityNote = U32(data, 12),
            MidiPitchFraction = U32(data, 16),
            SmpteFormat = U32(data, 20),
            SmpteOffset = U32(data, 24)
        };
        return new(data.ToArray(), metadata, loops, (int)extraOffset, (int)extraLength);
    }

    /// <summary>Rejects reversed, empty, negative, or out-of-duration loops without interpreting the stored loop type.</summary>
    internal static void ValidateLoop(WavLoop loop, int frames)
    {
        if (loop.StartFrame < 0 || loop.EndFrameExclusive <= loop.StartFrame || loop.EndFrameExclusive > frames)
        {
            throw new InvalidDataException("A sampler loop lies outside the complete PCM frame range.");
        }
    }
    /// <summary>Reads one already bounded sampler field.</summary>
    private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
    /// <summary>Writes one already bounded sampler field.</summary>
    private static void Put(Span<byte> data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data[offset..], value);
}
