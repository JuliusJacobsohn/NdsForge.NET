# WAV audio interchange {#wav_audio}

`NdsForge.Audio.Wav` is an optional adapter package. It depends only on
`NdsForge.Nitro`; the core Image library and native Nitro codecs do not depend on
it. It performs managed, in-memory conversion without starting external tools or
using a host playback library.

```shell
dotnet add package NdsForge.Audio.Wav
```

## Export native audio

```csharp
using NdsForge.Audio.Wav;
using NdsForge.Nitro.Audio;

StrmFile stream = StrmFile.Parse(File.ReadAllBytes("music.strm"));
WavFile wav = NitroWavAdapter.FromStrm(stream);
File.WriteAllBytes("music.wav", wav.WritePreserved());
```

`FromWave`, `FromSwav`, and `FromStrm` decode one pass of native audio. They retain
the sample rate, meaningful frame count, mono/stereo channel order, and active
loop start/end. They do not repeat loops. Stream blocks use independent ADPCM
states and do not add unused final nibbles to the duration.

Output defaults to signed sixteen-bit PCM. `NitroWavExportOptions.Encoding` can
select unsigned eight-bit PCM, explicitly discarding the low eight bits of each
decoded value. WAV eight-bit silence is 128; DS native eight-bit silence is zero.
The adapter converts this representation rather than copying those bytes.

`AdpcmClipping` defaults to `NintendoDs`. Select `Signed16` when a consuming tool
uses the full signed-sixteen subtraction range. The choice can change saturated
negative ADPCM samples; it does not affect PCM input. The native sound timer,
inactive loop fields, ADPCM initial state, block layout, and container padding are
not WAV sample metadata and do not survive semantic conversion.

Active native loops become forward, integer, indefinitely repeating `smpl` loops.
The WAV model uses exclusive loop ends like the native APIs; serialization stores
the inclusive end by subtracting one. Export derives the sampler period in
nanoseconds by integer division and uses MIDI unity note 60. These defaults are
not a claim to preserve a native instrument's pitch or envelope settings.

## Import WAV samples

```csharp
WavFile input = WavFile.Parse(File.ReadAllBytes("edited.wav"));
StrmFile output = NitroWavAdapter.ToStrm(input, NitroWaveEncoding.ImaAdpcm,
    new StrmCreateOptions { BlockByteLength = 512 });
File.WriteAllBytes("edited.strm", output.WritePreserved());
```

`ToStrm` accepts mono or stereo and retains the exact input frame count, including
odd ADPCM durations. `ToWave` and `ToSwav` require mono input. All imports require
a sample rate that fits the native sixteen-bit field. No implicit resampling,
stereo-to-mono mixing, or speaker reassignment occurs. An extensible WAV can use
an unspecified mask, front-center mono, or front-left/right stereo for import.
Other valid speaker masks remain inspectable in `WavFile` but require an explicit
mapping outside this adapter.

Native creation options select the output encoding policy, timer, and limits.
Omitted timers use the native creation API's rate-based defaults; WAV has no
equivalent native timer field. PCM16 preserves imported sample values exactly.
PCM8 discards low bits, and ADPCM encoding is lossy. An export/import cycle is
therefore not a promise of original compressed bytes or original Image identity.

Word-counted native waves require sample counts divisible by four for PCM8, two
for PCM16, or eight for ADPCM. They reject incomplete words unless
`NitroWaveCreateOptions.PadFinalWord` is explicitly enabled. Padding repeats the
last input sample before encoding, extending duration and any active loop end.
Native wave loop starts must also satisfy the selected encoding's word alignment.
Stream loop starts do not have that restriction.

The default `WavLoopImportPolicy.Preserve` accepts at most one forward, integer,
infinite loop whose end equals the WAV duration. Multiple, alternating, backward,
fractional, finite-repeat, or early-ending loops fail explicitly. A conflicting
native `LoopStartSample` request also fails. `Ignore` deliberately discards the WAV
loops; an explicitly supplied native loop still applies. Non-loop sampler tuning,
identifiers, synchronization fields, and opaque metadata are not applied to the
native samples.

## Inspect, preserve, and create WAV files

`WavFile.Parse` supports little-endian RIFF/WAVE with unsigned eight-bit or signed
sixteen-bit integer PCM, one or two channels, and complete frames. It accepts
standard PCM format records and full-precision WAVEFORMATEXTENSIBLE PCM records.
`Decode` returns signed sixteen-bit values in frame-major order: left then right
for each stereo frame. Unsigned PCM8 is normalized as `(value - 128) * 256`.

Direct chunks can occur in arbitrary order. `Chunks` retains identifiers, offsets,
payloads, and individual WORD-alignment bytes. Unknown chunks, format extensions,
sampler-specific data, and bytes outside the declared RIFF extent survive
`WritePreserved` exactly. Nested content in an unknown chunk is opaque; it is not
recursively interpreted. Duplicate format, data, or sampler chunks are rejected
because they make the interpreted representation ambiguous.

`WavSampler` exposes raw identification/tuning fields, ordered loop records, and
opaque sampler-specific data. Unknown loop types remain lossless. Every loop
must have a nonnegative start and a later exclusive end within the file's frame
count. Optional uninterpreted bytes after the declared sampler-specific data are
also retained. `WavSampler.Create` constructs metadata without requiring a native
DS representation.

`WavFile.Create` writes deterministic PCM WAV bytes: `fmt `, then `data`, then an
optional `smpl` chunk, with zero alignment bytes. `WavWriteOptions` selects sample
width, optional extensible format and speaker mask, sampler metadata, and limits.
Creation is canonical; it does not carry unknown source chunks automatically.
Preserving an existing file and creating a new file are distinct operations.

## Limits and unsupported formats

Default parsing/creation limits are 128 MiB of complete stored input/output,
thirty-two mebisamples across all channels, 4096 direct chunks, and 1024 sampler
loops. `WavReadOptions` controls these limits. `Decode` has its own output-value
ceiling, also thirty-two mebisamples by default. Export checks the complete output
size before native decoding. Import also obeys the native creation limits.
Array limits apply to individual buffers, not the sum of all simultaneous copies.
Use lower limits for untrusted uploads.

By default, parsing accepts a final odd-sized chunk whose declared RIFF extent
omits its alignment byte. `HasOmittedFinalPadding` exposes that condition;
`AllowMissingFinalPadding = false` rejects it. This allowance applies only to the
final chunk. Canonical creation always writes the required padding, and strict
parsing accepts its output.

Truncated chunks, inconsistent byte rates or frame alignment, invalid extensible
headers, out-of-range loops, overflowing sizes, and configured-limit violations
fail before unsafe slicing or unbounded allocation. This package does not support
floating-point or compressed WAV, 24/32-bit PCM, partial-precision extensible PCM,
multichannel remixing, RIFX, RF64, resampling, sequencing, or playback. Native
SWAR/SDAT archive navigation is a separate feature.
