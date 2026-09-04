using System.Buffers.Binary;

namespace NdsForge.Nitro.Audio;

/// <summary>Represents one bounded native mono sample block shared by SWAV files and SWAR archives.</summary>
public sealed class NitroWave
{
    private readonly byte[] _source;

    /// <summary>Publishes validated metadata and detached sample storage.</summary>
    private NitroWave(byte[] source, int byteCount, int sampleCount)
    {
        _source = source;
        Encoding = (NitroWaveEncoding)source[0];
        RawLoopFlag = source[1];
        SampleRate = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(2));
        Timer = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(4));
        LoopStartWords = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(6));
        RemainingWords = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(8));
        EncodedData = source.AsMemory(12, byteCount);
        SampleCount = sampleCount;
    }

    /// <summary>Gets the native sample encoding.</summary>
    public NitroWaveEncoding Encoding { get; }
    /// <summary>Gets the original loop flag; every nonzero value enables looping.</summary>
    public byte RawLoopFlag { get; }
    /// <summary>Gets whether the stored loop flag is nonzero.</summary>
    public bool IsLooping => RawLoopFlag != 0;
    /// <summary>Gets the declared samples per second; no resampling is performed.</summary>
    public ushort SampleRate { get; }
    /// <summary>Gets the independent stored sound timer value, without recomputing it on reads.</summary>
    public ushort Timer { get; }
    /// <summary>Gets the stored loop offset in four-byte words, including any ADPCM state-header word.</summary>
    public ushort LoopStartWords { get; }
    /// <summary>Gets the stored word count after the loop offset.</summary>
    public uint RemainingWords { get; }
    /// <summary>Gets all declared encoded bytes, including the ADPCM state header but excluding trailing padding.</summary>
    public ReadOnlyMemory<byte> EncodedData { get; }
    /// <summary>Gets the total number of stored sample values, excluding the ADPCM state header.</summary>
    public int SampleCount { get; }
    /// <summary>Gets the active loop's first decoded sample, or null when looping is disabled.</summary>
    public int? LoopStartSample => IsLooping ? WordToSample(LoopStartWords, Encoding) : null;
    /// <summary>Gets the active loop's exclusive end, or null when looping is disabled.</summary>
    public int? LoopEndSample => IsLooping ? SampleCount : null;

    /// <summary>Reads a twelve-byte wave header and its word-counted payload, retaining any following bytes.</summary>
    /// <param name="data">A complete bounded sample block, without a standalone SWAV envelope.</param>
    /// <param name="options">Input and sample-count limits.</param>
    /// <returns>A detached immutable wave with validated encoded state and loop bounds.</returns>
    public static NitroWave ParseSampleBlock(ReadOnlySpan<byte> data, NitroWaveReadOptions? options = null)
    {
        options ??= new();
        options.Validate();
        if (data.Length > options.MaximumInputBytes) { throw new InvalidDataException("The wave input limit was exceeded."); }
        if (data.Length < 12) { throw new InvalidDataException("A wave requires a twelve-byte sample header."); }
        NitroWaveEncoding encoding = (NitroWaveEncoding)data[0];
        if (!Enum.IsDefined(encoding)) { throw new InvalidDataException("The wave encoding is unsupported."); }
        if (BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) == 0) { throw new InvalidDataException("The wave sample rate is zero."); }
        int loopWords = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        long words = loopWords + (long)BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        long byteCount = words * 4;
        if (byteCount > data.Length - 12) { throw new InvalidDataException("The declared wave payload is truncated."); }
        int samples = ValidateSamples(data.Slice(12, (int)byteCount), encoding, loopWords, data[1], options.MaximumSamples);
        return new(data.ToArray(), (int)byteCount, samples);
    }

    /// <summary>Creates a wave from complete encoded words and explicit stored metadata, without re-encoding.</summary>
    /// <param name="encodedData">Word-aligned encoded samples, including an ADPCM state header when applicable.</param>
    /// <param name="encoding">Native sample representation.</param>
    /// <param name="sampleRate">Nonzero declared samples per second.</param>
    /// <param name="timer">Independent stored timer value.</param>
    /// <param name="loopStartWords">Stored word offset; may be nonzero even with looping disabled.</param>
    /// <param name="rawLoopFlag">Raw loop flag, zero to disable looping.</param>
    /// <param name="options">Input and sample-count limits applied to the complete generated block.</param>
    /// <returns>A validated native wave with no extra sample-block padding.</returns>
    public static NitroWave CreateEncoded(ReadOnlySpan<byte> encodedData, NitroWaveEncoding encoding,
        ushort sampleRate, ushort timer, ushort loopStartWords = 0, byte rawLoopFlag = 0, NitroWaveReadOptions? options = null)
    {
        options ??= new();
        options.Validate();
        if (!Enum.IsDefined(encoding)) { throw new ArgumentOutOfRangeException(nameof(encoding)); }
        if (encodedData.Length % 4 != 0 || loopStartWords > encodedData.Length / 4)
        {
            throw new ArgumentException("Encoded wave bytes must be complete words with a contained loop offset.", nameof(encodedData));
        }
        if (encodedData.Length + 12L > options.MaximumInputBytes) { throw new InvalidDataException("The wave input limit was exceeded."); }
        if (sampleRate == 0) { throw new InvalidDataException("The wave sample rate is zero."); }
        _ = ValidateSamples(encodedData, encoding, loopStartWords, rawLoopFlag, options.MaximumSamples);
        byte[] block = new byte[encodedData.Length + 12];
        block[0] = (byte)encoding;
        block[1] = rawLoopFlag;
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(2), sampleRate);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(4), timer);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(6), loopStartWords);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(8), (uint)(encodedData.Length / 4 - loopStartWords));
        encodedData.CopyTo(block.AsSpan(12));
        return ParseSampleBlock(block, options);
    }

    /// <summary>Creates a native wave from PCM16 samples with explicit word-padding and loop policies.</summary>
    /// <param name="samples">Unpadded signed mono input samples.</param>
    /// <param name="encoding">Desired native sample representation.</param>
    /// <param name="sampleRate">Nonzero declared samples per second.</param>
    /// <param name="options">Encoding, padding, loop, timer, and sample-limit choices.</param>
    /// <returns>A validated wave whose sample count includes explicitly requested final-word padding.</returns>
    public static NitroWave Create(ReadOnlySpan<short> samples, NitroWaveEncoding encoding, ushort sampleRate, NitroWaveCreateOptions? options = null)
    {
        options ??= new();
        ArgumentNullException.ThrowIfNull(options.EncodingOptions);
        options.EncodingOptions.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaximumSamples);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaximumSamples, Array.MaxLength / 2);
        ArgumentOutOfRangeException.ThrowIfZero(sampleRate);
        if (!Enum.IsDefined(encoding)) { throw new ArgumentOutOfRangeException(nameof(encoding)); }
        int quantum = encoding switch { NitroWaveEncoding.Pcm8 => 4, NitroWaveEncoding.Pcm16 => 2, _ => 8 };
        long count = (samples.Length + (long)quantum - 1) / quantum * quantum;
        if (count > options.MaximumSamples) { throw new InvalidDataException("The wave sample-count limit was exceeded."); }
        if (count != samples.Length && !options.PadFinalWord) { throw new ArgumentException("The samples do not fill complete encoded words.", nameof(samples)); }
        long encodedCount = encoding switch { NitroWaveEncoding.Pcm8 => count, NitroWaveEncoding.Pcm16 => count * 2, _ => 4 + count / 2 };
        if (encodedCount > options.EncodingOptions.MaximumOutputBytes || encodedCount + 12 > Array.MaxLength)
        {
            throw new InvalidDataException("The encoded wave byte limit was exceeded.");
        }
        int timer = options.Timer ?? (16_756_991 / sampleRate);
        if (timer > ushort.MaxValue) { throw new ArgumentOutOfRangeException(nameof(sampleRate), "The derived timer does not fit; supply an explicit timer."); }
        int loopWords = encoding == NitroWaveEncoding.ImaAdpcm ? 1 : 0;
        if (options.LoopStartSample is int loop)
        {
            if (loop < 0 || loop >= samples.Length || loop % quantum != 0) { throw new ArgumentOutOfRangeException(nameof(options), "The loop must be a word-aligned input sample."); }
            loopWords += loop / quantum;
            if (loopWords > ushort.MaxValue) { throw new ArgumentOutOfRangeException(nameof(options), "The loop word offset exceeds sixteen bits."); }
        }
        if (count != samples.Length)
        {
            short[] padded = new short[(int)count];
            samples.CopyTo(padded);
            padded.AsSpan(samples.Length).Fill(samples[^1]);
            samples = padded;
        }
        byte[] encoded = NitroWaveCodec.Encode(samples, encoding, options.EncodingOptions);
        return CreateEncoded(encoded, encoding, sampleRate, (ushort)timer, (ushort)loopWords,
            (byte)(options.LoopStartSample.HasValue ? 1 : 0),
            new() { MaximumInputBytes = checked(encoded.Length + 12), MaximumSamples = options.MaximumSamples });
    }

    /// <summary>Decodes every declared sample without repeating the active loop.</summary>
    /// <param name="options">Sample allocation limit and explicit saturation policy.</param>
    /// <returns>One pass of signed sixteen-bit mono sample values.</returns>
    public short[] Decode(NitroWaveDecodeOptions? options = null) => NitroWaveCodec.Decode(EncodedData.Span, Encoding, SampleCount, options);

    /// <summary>Returns the exact sample block or just its header and declared encoded data.</summary>
    /// <param name="preservePadding">Whether to retain all bytes after the declared payload; defaults to true.</param>
    /// <returns>A detached twelve-byte-header-prefixed sample block.</returns>
    public byte[] WriteSampleBlock(bool preservePadding = true) => GetSampleBlock(preservePadding).ToArray();

    /// <summary>Allows paired container writers to preflight and copy without an intermediate sample-block allocation.</summary>
    internal ReadOnlySpan<byte> GetSampleBlock(bool preservePadding) => _source.AsSpan(0, preservePadding ? _source.Length : EncodedData.Length + 12);

    /// <summary>Validates the encoded state, derived duration, and active loop before allocating detached storage.</summary>
    private static int ValidateSamples(ReadOnlySpan<byte> encoded, NitroWaveEncoding encoding, int loopWords, byte loopFlag, int maximumSamples)
    {
        if (encoding == NitroWaveEncoding.ImaAdpcm && (encoded.Length < 4 || (encoded[2] & 127) > 88))
        {
            throw new InvalidDataException("The wave ADPCM state header is missing or has an invalid index.");
        }
        long samples = encoding switch { NitroWaveEncoding.Pcm8 => encoded.Length, NitroWaveEncoding.Pcm16 => encoded.Length / 2, _ => (encoded.Length - 4L) * 2 };
        if (samples > maximumSamples) { throw new InvalidDataException("The wave sample-count limit was exceeded."); }
        if (loopFlag != 0 && (WordToSample(loopWords, encoding) < 0 || WordToSample(loopWords, encoding) >= samples))
        {
            throw new InvalidDataException("The active wave loop must start within the decoded samples.");
        }
        return (int)samples;
    }

    /// <summary>Translates word offsets without including the ADPCM state header as samples.</summary>
    private static int WordToSample(int words, NitroWaveEncoding encoding) => encoding switch
    {
        NitroWaveEncoding.Pcm8 => words * 4,
        NitroWaveEncoding.Pcm16 => words * 2,
        _ => (words - 1) * 8,
    };
}
