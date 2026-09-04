using System.Buffers.Binary;

namespace NdsForge.Nitro.Audio;

/// <summary>Provides a bounded standalone SWAV envelope and its native mono wave.</summary>
public sealed class SwavFile
{
    private readonly byte[] _source;

    /// <summary>Retains the validated container layout and sample block.</summary>
    private SwavFile(byte[] source, int headerLength, int declaredLength, NitroWave wave)
    {
        _source = source;
        HeaderLength = headerLength;
        DeclaredLength = declaredLength;
        Wave = wave;
        ByteOrderMarker = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(4));
    }

    /// <summary>Gets the stored standard-file byte-order marker; sample fields are little-endian.</summary>
    public ushort ByteOrderMarker { get; }
    /// <summary>Gets the standard-file header length, including any preserved extension.</summary>
    public int HeaderLength { get; }
    /// <summary>Gets the file-header-declared byte length, excluding outer allocation padding.</summary>
    public int DeclaredLength { get; }
    /// <summary>Gets the contained mono sample block.</summary>
    public NitroWave Wave { get; }

    /// <summary>Parses and copies one standalone SWAV, retaining header extensions and both inner and outer padding.</summary>
    /// <param name="data">Complete SWAV bytes, optionally followed by allocation padding.</param>
    /// <param name="options">Input-byte and wave-sample limits.</param>
    /// <returns>A detached container with validated block boundaries.</returns>
    public static SwavFile Parse(ReadOnlySpan<byte> data, NitroWaveReadOptions? options = null)
    {
        options ??= new();
        options.Validate();
        if (data.Length > options.MaximumInputBytes) { throw new InvalidDataException("The SWAV input limit was exceeded."); }
        if (data.Length < 36 || !data[..4].SequenceEqual("SWAV"u8)) { throw new InvalidDataException("The SWAV header is missing or truncated."); }
        ushort marker = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        if (marker is not (0xFEFF or 0xFFFE) || BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) != 0x0100)
        {
            throw new InvalidDataException("The SWAV marker or version is unsupported.");
        }
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        int header = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        if (length > data.Length || header < 16 || header + 20L > length || BinaryPrimitives.ReadUInt16LittleEndian(data[14..]) != 1)
        {
            throw new InvalidDataException("The SWAV size, header length, or block count is invalid.");
        }
        if (!data.Slice(header, 4).SequenceEqual("DATA"u8) || BinaryPrimitives.ReadUInt32LittleEndian(data[(header + 4)..]) != length - header)
        {
            throw new InvalidDataException("The SWAV DATA block does not cover the declared file.");
        }
        NitroWave wave = NitroWave.ParseSampleBlock(data.Slice(header + 8, (int)length - header - 8), options);
        return new(data.ToArray(), header, (int)length, wave);
    }

    /// <summary>Wraps an existing native wave in a standard standalone envelope.</summary>
    /// <param name="wave">Validated native wave; extra sample-block padding is omitted.</param>
    /// <param name="options">Output allocation limit; preservation is inapplicable without a source envelope.</param>
    /// <returns>A minimal version-0100 SWAV with a sixteen-byte header.</returns>
    public static SwavFile Create(NitroWave wave, SwavWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(wave);
        byte[] bytes = SwavFileBuilder.Compose(null, wave, options ?? new());
        return Parse(bytes, new() { MaximumInputBytes = bytes.Length, MaximumSamples = wave.SampleCount });
    }

    /// <summary>Creates an isolated wave-replacement plan with explicit preservation choices.</summary>
    /// <returns>A builder initially retaining the source wave.</returns>
    public SwavFileBuilder CreateBuilder() => new(this);

    /// <summary>Returns every original byte, including header extensions and allocation padding.</summary>
    /// <returns>A detached exact copy of the source input.</returns>
    public byte[] WritePreserved() => (byte[])_source.Clone();

    /// <summary>Exposes source layout to the paired writer without exposing a writable public buffer.</summary>
    internal ReadOnlyMemory<byte> Source => _source;
}
