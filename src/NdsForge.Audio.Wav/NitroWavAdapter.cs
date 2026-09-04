using NdsForge.Nitro.Audio;

namespace NdsForge.Audio.Wav;

/// <summary>Converts native waves and streams to and from bounded PCM WAV without resampling, mixing, or playback.</summary>
public static class NitroWavAdapter
{
    /// <summary>Exports a native mono wave, converting signed PCM8 and describing rather than repeating its loop.</summary>
    /// <param name="wave">Validated native samples and metadata.</param>
    /// <param name="options">WAV storage, clipping, and allocation choices.</param>
    /// <returns>A WAV retaining sample rate, duration, and the active loop; native timer and storage metadata are not carried over.</returns>
    public static WavFile FromWave(NitroWave wave, NitroWavExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(wave);
        options ??= new();
        WavWriteOptions write = PrepareExport(wave.SampleCount, wave.SampleRate, wave.LoopStartSample, options);
        short[] samples = wave.Decode(new() { MaximumSamples = options.Limits.MaximumSampleValues, AdpcmClipping = options.AdpcmClipping });
        return WavFile.Create(samples, 1, wave.SampleRate, write);
    }

    /// <summary>Exports the mono native sample block inside a standalone SWAV.</summary>
    /// <param name="file">Validated standalone wave.</param>
    /// <param name="options">WAV storage, clipping, and allocation choices.</param>
    /// <returns>A WAV with one pass of the declared samples and its active loop.</returns>
    public static WavFile FromSwav(SwavFile file, NitroWavExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        return FromWave(file.Wave, options);
    }

    /// <summary>Exports a mono/stereo stream with independent block decoding and frame-major channel ordering.</summary>
    /// <param name="file">Validated native stream.</param>
    /// <param name="options">WAV storage, clipping, and allocation choices.</param>
    /// <returns>A WAV retaining the exact declared frame count, sample rate, channels, and active loop.</returns>
    public static WavFile FromStrm(StrmFile file, NitroWavExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        options ??= new();
        WavWriteOptions write = PrepareExport(file.SampleValueCount, file.SampleRate, file.LoopStartSample, options, file.SampleCount);
        short[] samples = file.Decode(new() { MaximumSamples = options.Limits.MaximumSampleValues, AdpcmClipping = options.AdpcmClipping });
        return WavFile.Create(samples, file.ChannelCount, file.SampleRate, write);
    }

    /// <summary>Imports a mono WAV into a native word-counted wave using explicit encoding, padding, and loop policies.</summary>
    /// <param name="file">Validated WAV; sampler tuning and non-loop metadata are not applied to the samples.</param>
    /// <param name="encoding">Native output encoding; PCM8 discards low sample bits and ADPCM is lossy.</param>
    /// <param name="options">Native encoding, timer, limits, and explicit final-word padding; padding extends duration and loop end.</param>
    /// <param name="loopPolicy">Whether to preserve representable WAV loops or explicitly ignore them.</param>
    /// <returns>A native mono wave; unsupported rates, speaker assignments, loops, and word alignment are not silently changed.</returns>
    public static NitroWave ToWave(WavFile file, NitroWaveEncoding encoding, NitroWaveCreateOptions? options = null,
        WavLoopImportPolicy loopPolicy = WavLoopImportPolicy.Preserve)
    {
        ValidateImport(file, encoding);
        if (file.ChannelCount != 1) { throw new NotSupportedException("Native waves require mono input; channels are never mixed implicitly."); }
        options ??= new();
        int? loop = ImportLoop(file, options.LoopStartSample, loopPolicy);
        short[] samples = file.Decode(options.MaximumSamples);
        return NitroWave.Create(samples, encoding, (ushort)file.SampleRate, options with { LoopStartSample = loop });
    }

    /// <summary>Imports a mono WAV and wraps its newly encoded native wave in a standalone SWAV.</summary>
    /// <param name="file">Validated mono WAV.</param>
    /// <param name="encoding">Native output encoding.</param>
    /// <param name="options">Native wave creation choices, including explicit padding when necessary.</param>
    /// <param name="loopPolicy">Preservation or explicit disregard of WAV sampler loops.</param>
    /// <param name="writeOptions">Standalone envelope allocation choices.</param>
    /// <returns>A canonical standalone SWAV; this is a semantic conversion, not an encoded-byte round trip.</returns>
    public static SwavFile ToSwav(WavFile file, NitroWaveEncoding encoding, NitroWaveCreateOptions? options = null,
        WavLoopImportPolicy loopPolicy = WavLoopImportPolicy.Preserve, SwavWriteOptions? writeOptions = null)
        => SwavFile.Create(ToWave(file, encoding, options, loopPolicy), writeOptions);

    /// <summary>Imports mono/stereo WAV frames into independently encoded native stream blocks.</summary>
    /// <param name="file">Validated WAV with standard or unspecified speaker positions.</param>
    /// <param name="encoding">Native output encoding; stream creation retains odd ADPCM frame counts.</param>
    /// <param name="options">Native block sizing, timer, limits, encoding, and optional explicit loop.</param>
    /// <param name="loopPolicy">Preservation or explicit disregard of WAV sampler loops.</param>
    /// <returns>A stream with unchanged rate, channels, and frame count, without implicit resampling or loop truncation.</returns>
    public static StrmFile ToStrm(WavFile file, NitroWaveEncoding encoding, StrmCreateOptions? options = null,
        WavLoopImportPolicy loopPolicy = WavLoopImportPolicy.Preserve)
    {
        ValidateImport(file, encoding);
        options ??= new();
        ArgumentNullException.ThrowIfNull(options.Limits);
        int? loop = ImportLoop(file, options.LoopStartSample, loopPolicy);
        short[] samples = file.Decode(options.Limits.MaximumSampleValues);
        return StrmFile.Create(samples, file.ChannelCount, encoding, (ushort)file.SampleRate, options with { LoopStartSample = loop });
    }

    /// <summary>Bounds the complete WAV and sample arrays before decoding any native payload.</summary>
    private static WavWriteOptions PrepareExport(int sampleValues, ushort rate, int? loop, NitroWavExportOptions options, int? frames = null)
    {
        ArgumentNullException.ThrowIfNull(options.Limits);
        options.Limits.Validate();
        if (!Enum.IsDefined(options.Encoding)) { throw new ArgumentOutOfRangeException(nameof(options), "The WAV PCM representation is unsupported."); }
        if (!Enum.IsDefined(options.AdpcmClipping)) { throw new ArgumentOutOfRangeException(nameof(options), "The ADPCM clipping policy is unsupported."); }
        int width = options.Encoding == WavPcmEncoding.Unsigned8 ? 1 : 2;
        long payload = sampleValues * (long)width;
        long length = 44 + (options.UseExtensibleFormat ? 24 : 0) + payload + payload % 2 + (loop.HasValue ? 68 : 0);
        if (length > options.Limits.MaximumInputBytes || sampleValues > options.Limits.MaximumSampleValues
            || options.Limits.MaximumChunks < (loop.HasValue ? 3 : 2) || (loop.HasValue && options.Limits.MaximumLoops == 0))
        {
            throw new InvalidDataException("The complete WAV output, sample, chunk, or loop limit was exceeded.");
        }
        WavSampler? sampler = loop is int start ? WavSampler.Create([new(0, 0, start, frames ?? sampleValues)],
            new() { SamplePeriodNanoseconds = 1_000_000_000u / rate }, options: options.Limits) : null;
        return new() { Encoding = options.Encoding, UseExtensibleFormat = options.UseExtensibleFormat, Sampler = sampler, Limits = options.Limits };
    }

    /// <summary>Rejects unsupported rates and speaker semantics rather than silently resampling or reinterpreting channels.</summary>
    private static void ValidateImport(WavFile file, NitroWaveEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!Enum.IsDefined(encoding)) { throw new ArgumentOutOfRangeException(nameof(encoding)); }
        if (file.SampleRate > ushort.MaxValue) { throw new NotSupportedException("The WAV rate exceeds native DS storage; explicit resampling is required."); }
        uint expectedMask = file.ChannelCount == 1 ? 4u : 3u;
        if (file.ChannelMask != 0 && file.ChannelMask != expectedMask) { throw new NotSupportedException("The WAV speaker positions require an explicit channel mapping."); }
    }

    /// <summary>Maps only native-representable loop behavior and checks conflicting explicit loop requests.</summary>
    private static int? ImportLoop(WavFile file, int? requested, WavLoopImportPolicy policy)
    {
        if (!Enum.IsDefined(policy)) { throw new ArgumentOutOfRangeException(nameof(policy)); }
        if (policy == WavLoopImportPolicy.Ignore || file.Sampler is null || file.Sampler.Loops.Count == 0) { return requested; }
        if (file.Sampler.Loops.Count != 1) { throw new NotSupportedException("Native audio cannot retain multiple WAV loops."); }
        WavLoop loop = file.Sampler.Loops[0];
        if (loop.Type != 0 || loop.Fraction != 0 || loop.PlayCount != 0 || loop.EndFrameExclusive != file.FrameCount)
        {
            throw new NotSupportedException("Native audio requires a forward integer infinite loop ending at the sample duration.");
        }
        if (requested.HasValue && requested.Value != loop.StartFrame) { throw new ArgumentException("The requested native loop conflicts with the WAV loop.", nameof(requested)); }
        return loop.StartFrame;
    }
}
