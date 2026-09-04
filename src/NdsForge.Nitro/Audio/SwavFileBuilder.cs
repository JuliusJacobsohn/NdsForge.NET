using System.Buffers.Binary;

namespace NdsForge.Nitro.Audio;

/// <summary>Replaces a standalone wave while preserving its envelope or producing a minimal deterministic file.</summary>
public sealed class SwavFileBuilder
{
    private readonly SwavFile _source;

    /// <summary>Starts with a validated immutable source wave.</summary>
    internal SwavFileBuilder(SwavFile source) { _source = source; Wave = source.Wave; }

    /// <summary>Gets or sets the replacement wave, including its stored sample rate, timer, and loop metadata.</summary>
    public NitroWave Wave { get; set; }

    /// <summary>Composes and reparses the complete file before returning any bytes.</summary>
    /// <param name="options">Preservation and output allocation choices.</param>
    /// <returns>A detached, validated SWAV file.</returns>
    public byte[] Build(SwavWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(Wave);
        options ??= new();
        byte[] bytes = Compose(_source, Wave, options);
        _ = SwavFile.Parse(bytes, new() { MaximumInputBytes = options.MaximumOutputBytes, MaximumSamples = Wave.SampleCount });
        return bytes;
    }

    /// <summary>Preserves only selected source regions, updating the two size declarations atomically.</summary>
    internal static byte[] Compose(SwavFile? source, NitroWave wave, SwavWriteOptions options)
    {
        options.Validate();
        bool preserve = options.PreserveSourceLayout && source is not null;
        int header = preserve ? source!.HeaderLength : 16;
        int trailing = preserve ? source!.Source.Length - source.DeclaredLength : 0;
        ReadOnlySpan<byte> block = wave.GetSampleBlock(preserve);
        long declared = header + 8L + block.Length;
        long length = declared + trailing;
        if (length > options.MaximumOutputBytes) { throw new InvalidDataException("The SWAV output limit was exceeded."); }
        byte[] result = new byte[(int)length];
        if (preserve) { source!.Source.Span[..header].CopyTo(result); }
        else
        {
            "SWAV"u8.CopyTo(result);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), 0xFEFF);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), 0x0100);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), 16);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14), 1);
        }
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8), (int)declared);
        "DATA"u8.CopyTo(result.AsSpan(header));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(header + 4), (int)declared - header);
        block.CopyTo(result.AsSpan(header + 8));
        if (preserve) { source!.Source.Span[source.DeclaredLength..].CopyTo(result.AsSpan((int)declared)); }
        return result;
    }
}
