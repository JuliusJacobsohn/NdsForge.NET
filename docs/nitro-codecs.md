# Nitro codecs {#nitro_codecs}

`NdsForge.Nitro` is a separate, dependency-free package for reusable Nintendo DS software formats that do not require the ROM-image object model. Applications that only inspect, edit, or build cartridge images can continue to reference `NdsForge` alone.

## Forward BIOS compression

The shared compression envelope records a type byte and exact decoded length. `NitroCompression.TryInspect` reads that metadata without allocating output, while `NitroCompression.Decompress` dispatches LZ10 (`0x10`), LZ11 (`0x11`), four- or eight-bit Huffman (`0x24` and `0x28`), and run-length (`0x30`) streams.

```csharp
using NdsForge.Nitro.Compression;

byte[] stored = File.ReadAllBytes("resource.bin");
if (NitroCompression.TryInspect(stored, out NitroCompressionInfo info))
{
    byte[] decoded = NitroCompression.Decompress(stored, maximumDecodedLength: 64 * 1024 * 1024);
    Console.WriteLine($"{info.Type}: {stored.Length} -> {decoded.Length} bytes");
}
```

`Lz10Codec`, `Lz11Codec`, and `RleCodec` also expose deterministic encoders.
Huffman is currently decode-only. All decoders reject truncated tokens, invalid
look-behinds or trees, and outputs above the caller-defined allocation limit.
Huffman validates every reachable branch and leaf before decoding, including
branches not selected by the bitstream, without recursively expanding shared
subtrees. Run-length blocks cannot exceed the declared output; LZ10 and LZ11
clip their final back-reference to the header-declared length.

The private compatibility suite locks accepted candidates to reviewed decoded
digests and supplements corpus coverage with hand-authored grammar vectors and
cross-decoding of deterministic encoder output. A matching first byte alone
does not establish that an allocation is a compression stream.

The expanded corpus contains 81,430 accepted LZ10, 11,538 LZ11, 4,158 run-length,
11 four-bit Huffman, and 191 eight-bit Huffman candidates. The independently
checked output matches the declared-length bytes in every case, including one
LZ10 and one LZ11 input whose final back-reference is clipped. BLZ checks cover
58 compressed programs and 4,945 compressed overlays.

## NARC archives

`NarcArchive` reconciles the BTAF allocation array, optional BTNF filename hierarchy, and GMIF payload block. File IDs remain stable even when the filename table is absent or names cover only part of the allocation array. Names use a byte-preserving Latin-1 projection and exact, case-sensitive slash paths.

```csharp
using NdsForge.Nitro.Archives;

NarcArchive archive = NarcArchive.Parse(File.ReadAllBytes("resources.narc"));
NarcFile file = archive.FindFile("/images/icon.bin")
    ?? throw new FileNotFoundException();

byte[] rebuilt = archive.CreateBuilder()
    .ReplaceFile(file.Id, File.ReadAllBytes("icon.bin"))
    .Build(new NarcWriteOptions { PreserveSourceLayout = true });
File.WriteAllBytes("resources-edited.narc", rebuilt);
```

A no-op build is byte-identical, including allocation padding and bytes after the header-declared archive. Same-size replacements patch the preserved layout. Size-changing replacements deterministically rebuild the payload with caller-selected alignment and padding while retaining file IDs and the original filename table. Both standard header-marker representations are supported; block integers remain little-endian as required by the container.

The private compatibility suite covers 13,630 valid NARC allocations and all 940,219 contained files. It verifies exact preservation, canonical rebuild/reparse semantics, and a payload-and-path aggregate digest locked to a reviewed compatibility baseline.

## Wi-Fi utility archives

`WifiUtilityArchive` reads the plain asset container commonly stored as
`utility.bin`. Its sixteen-byte envelope contains filename-table and allocation-table
offsets and lengths; it has no magic or independently established version field.
The two observed table layouts do not imply two SDK versions. A leading `0x10`
commonly means the filename table begins at offset sixteen, not LZ10 compression.
Select this parser explicitly before trying generic compression on such an asset.

```csharp
using NdsForge.Nitro.Archives;

WifiUtilityArchive archive = WifiUtilityArchive.Parse(File.ReadAllBytes("utility.bin"));
foreach (WifiUtilityFile file in archive.Files)
    Console.WriteLine($"{file.Id}: {file.FullPath ?? "<unnamed>"}, {file.Data.Length} bytes");

byte[] replacementBytes = File.ReadAllBytes("replacement.bin");
byte[] rebuilt = archive.CreateBuilder()
    .ReplaceFile("/msg/example.bin", replacementBytes)
    .Build(new WifiUtilityWriteOptions
    {
        PreserveSourceLayout = false,
        TableAlignment = 32,
        FileAlignment = 32,
    });
```

All FAT identities are retained, including unnamed allocations, empty files, and
shared source intervals. The directory graph exposes parent and child identities,
declared first-file IDs, original subtable offsets, and byte-preserving Latin-1
name projections. Lookup uses exact, case-sensitive slash paths, not host paths.
Extract a file by copying its `Data` to an explicitly chosen safe destination;
the library does not turn archive names into host filesystem writes. Embedded SRLs,
compressed assets, and wireless protocol code remain opaque payloads.

`WritePreserved` and an unchanged builder reproduce every source byte, including
unknown gaps, table padding, and trailing material. Compatible same-sized payload
edits patch that layout. If a shared interval would unintentionally change another
file, the builder instead places allocations independently. Size changes and real
name changes use canonical rebuilding while retaining IDs and opaque filename-table
bytes, updating relative subtable offsets where necessary. Canonical builds do not retain unrelated physical gaps
or trailing material. `RenameFile` and `RenameDirectory` change final path segments
without moving entries or changing the hierarchy. Adding/removing entries and
creating a new hierarchy are not exposed by this initial API.

Canonical filename tables begin at sixteen. `TableAlignment` controls the FAT
start (power of two, minimum four); `FileAlignment` independently controls every
payload and the final output end. Both default to four, with zero padding.
Preservation takes precedence over these placement preferences. Every completed
builder output is reparsed before it is returned. Duplicate names, invalid IDs,
unreachable or cyclic directories, overlapping metadata, truncated records,
arithmetic overflow, and configured-limit violations fail explicitly.

Default read limits are 64 MiB of source bytes, 61,440 allocations, 4,096
directories, and depth 64. The default write ceiling is 64 MiB; limits describe
input/output allocations rather than total peak process memory. The private corpus
gate covers all 79 direct occurrences across 65 images, four distinct archives,
1,472 file records, and exact four-/32-byte canonical layout identities. Neutral
vectors additionally cover growth, shrinkage, renaming, empty and unnamed entries,
shared intervals, and malformed input. No wireless emulation, SDK-version inference,
or automatic decompression is implied.

## Native audio streams

`StrmFile` reads mono and stereo STRM containers using PCM8, PCM16, or DS
IMA-ADPCM. It exposes the sample rate and independent timer, raw flags, active
loop positions, block geometry, and complete per-channel encoded blocks.
`SampleCount` is the number of frames in each channel; `SampleValueCount` is
the interleaved output length. Loop ends are exclusive. `Decode` produces one
pass in frame order (left, right for stereo); `DecodeChannel` selects one channel.
Neither method repeats loops or resamples.

```csharp
using NdsForge.Nitro.Audio;

short[] stereo = [1000, -1000, 2000, -2000, 3000, -3000];
StrmFile stream = StrmFile.Create(stereo, 2, NitroWaveEncoding.ImaAdpcm, 22050,
    new StrmCreateOptions { BlockByteLength = 512, LoopStartSample = 1 });
byte[] stored = stream.WritePreserved();
short[] decoded = StrmFile.Parse(stored).Decode();
```

Creation accepts complete sample frames and stores their exact duration, including
odd ADPCM counts. Every channel block gets its own ADPCM state. Full block sizes
include the four-byte ADPCM header and must leave room for samples; PCM16 block
sizes must be even. Final channel blocks are adjacent without per-channel alignment.
Only the end of the DATA block is rounded to four bytes. An unused ADPCM high
nibble does not add a sample. Exact multiples do not create an extra empty final
block, although existing files containing one remain readable.
For single-block output, both block-size declarations describe that actual block
rather than the requested maximum full-block capacity.

`StrmCreateOptions.SampleEncoding` controls each block's predictor, step index,
clipping policy, and encoded-byte limit. An unspecified predictor uses that
channel block's first sample. The default timer is floor(16756991 / rate / 32),
not the standalone wave timer. An explicit timer allows metadata to be retained
independently of the rate. Empty nonlooping streams are representable; this does
not claim that hardware or playback software accepts empty audio.

An unchanged builder preserves every source byte, including header extensions,
reserved metadata, payload gaps, encoded padding, trailing file data, and allocation
padding. Metadata edits do not implicitly recalculate the timer. `ReplaceBlock`
requires a complete validated replacement with the existing slot's physical byte
length; use `Create` when changing the encoding, channels, or block geometry.

```csharp
StrmFileBuilder edit = stream.CreateBuilder();
edit.SampleRate = 16000;
edit.Timer = 32;
byte[] canonical = edit.Build(new StrmWriteOptions { PreserveSourceLayout = false });
```

Canonical rebuilding uses a sixteen-byte header, an eighty-byte HEAD block,
adjacent channel blocks, and zero final alignment. Defined metadata and the
standard reserved HEAD area are retained; extensions and other padding are
omitted. Legacy single-block ADPCM files can omit the state header from their
stored byte-length declarations. `ExcludesAdpcmStateHeaderFromLength` identifies
this case, and `GetBlock` still includes the complete header. Preservation retains
the raw declarations; canonical output normalizes them to header-inclusive lengths.
Canonical single-block output also normalizes the otherwise unused normal-block
declarations to the actual final-block size and sample count.

Parsing bounds input to 64 MiB, total decoded values to thirty-two mebisamples,
and blocks to one mebiblock per channel by default. `StrmReadOptions` controls
these limits; creation uses the same limits through `StrmCreateOptions.Limits`.
Decoding without options uses the stream's thirty-two-mebisample default. Passing
`NitroWaveDecodeOptions` explicitly uses that object's limit, which defaults to
sixteen mebisamples. Choose application-specific limits for untrusted uploads.
ADPCM uses the same explicit DS versus signed-16 clipping policies as raw waves.
WAV import/export, sound archive navigation, and playback are separate features.

## Raw audio samples

`NitroWaveCodec` converts native mono PCM8, PCM16, and DS IMA-ADPCM blocks
to and from signed sixteen-bit sample values. It does not parse SWAV/STRM
containers, infer sample rates or loops, play audio, resample, or read/write WAV
files. Those responsibilities remain separate from the raw sample codec.

```csharp
using NdsForge.Nitro.Audio;

short[] samples = [1000, 1100, 900, 800, 1000];
byte[] encoded = NitroWaveCodec.Encode(samples, NitroWaveEncoding.ImaAdpcm,
    new NitroWaveEncodeOptions { InitialStepIndex = 20 });
short[] decoded = NitroWaveCodec.Decode(encoded, NitroWaveEncoding.ImaAdpcm,
    sampleCount: samples.Length);
```

PCM16 is signed little-endian and lossless. PCM8 stores signed eight-bit values;
decoding multiplies them by 256. Encoding discards the low PCM16 byte, rounding
toward negative infinity. Thus each decoded value is between zero and 255 below
the input; it is not a nearest-value conversion.

ADPCM starts with a four-byte state header, followed by low-nibble-first samples.
The header predictor is signed, the step index is zero through eighty-eight,
and unused header bits do not affect decoding. The initial predictor is state,
not an additional output sample. Encoding defaults to the first input sample as
the predictor and index zero; callers may supply both explicitly. Empty ADPCM
input produces a state header, while empty PCM produces no bytes.

The default `NintendoDs` clipping policy saturates subtraction at -32767 and
addition at 32767. An initial -32768 can remain unchanged after a positive
zero-delta code. Select `NitroAdpcmClipping.Signed16` explicitly for tools that
instead saturate subtraction at -32768. Both encoding and decoding expose this
choice; the resulting samples, and sometimes encoded nibbles, can differ.

ADPCM encoding chooses the next representable sample with the smallest absolute
error; equal errors prefer the lower nibble. This policy is deterministic and
lossy. It does not claim globally optimal quality or byte parity with other
encoders. A final odd sample occupies the low nibble and leaves the high nibble
zero. Supply the meaningful `sampleCount` when decoding to exclude that padding.
With no count, decoding returns every stored sample, including encoded padding.

Decoded output defaults to a limit of sixteen mebisamples; encoded output
defaults to 64 MiB including the ADPCM header. Limits apply before allocating
the output array. Incomplete PCM16 values, truncated ADPCM headers, invalid
indices, unsupported modes, and impossible sample counts fail explicitly.
The native package has no host audio-file, playback, or resampling dependency.

## Standalone waves and native sample blocks

`SwavFile` reads standalone SWAV files; its `Wave` is a `NitroWave`, the same
twelve-byte-header-prefixed sample block used within SWAR archives. The wave
exposes encoded bytes, encoding, sample rate, independent stored timer, raw loop
flag, word counts, total sample count, and nullable active-loop sample positions.
`Decode` returns one pass through the wave, without repeating its loop.

```csharp
using NdsForge.Nitro.Audio;

SwavFile original = SwavFile.Parse(File.ReadAllBytes("sound.swav"));
short[] samples = original.Wave.Decode();

NitroWave replacement = NitroWave.Create(samples, NitroWaveEncoding.ImaAdpcm,
    original.Wave.SampleRate, new NitroWaveCreateOptions
    {
        PadFinalWord = true,
        LoopStartSample = 0,
        EncodingOptions = new NitroWaveEncodeOptions { InitialStepIndex = 20 },
    });
SwavFileBuilder builder = original.CreateBuilder();
builder.Wave = replacement;
File.WriteAllBytes("sound-edited.swav", builder.Build());
```

Stored wave lengths use four-byte words: four PCM8 samples, two PCM16 samples,
or eight ADPCM samples per data word. ADPCM additionally has one state-header
word. `LoopStartWords` includes that header; `LoopStartSample` does not. Active
loops must start inside the decoded duration, not at its exclusive end or within
the ADPCM header. Inactive loop offsets, noncanonical nonzero loop flags, reserved
ADPCM header bits, and independent timer values remain lossless.

Sample-based `Create` rejects incomplete final words by default. `PadFinalWord`
explicitly repeats the final input sample before encoding and increases the stored
duration to the next full word. Loop starts must already be word-aligned; they
are never rounded. The timer defaults to integer division of 16756991 by the
nonzero sample rate; supply `Timer` to retain another stored value. An unrepresentable
derived timer fails instead of wrapping. Empty nonlooping sample blocks can be
represented; parsing a wave does not imply that its length is hardware-playable.

`CreateEncoded` accepts already word-aligned encoded samples plus explicit rate,
timer, loop offset, and raw loop flag. `ParseSampleBlock` reads the shared wave
representation without a standalone envelope. `WriteSampleBlock` retains all
following padding by default; pass false for only the header and declared data.

`WritePreserved` and an unchanged SWAV builder retain every original byte,
including header extensions, inner wave padding, and outer allocation padding.
A replacement changes the entire wave block, including its own padding, while
retaining the source envelope extension and outer padding. Set
`PreserveSourceLayout = false` for a deterministic sixteen-byte-header envelope
without extensions or padding. Creating a new `SwavFile` also uses this canonical
layout. Both marker representations retain little-endian sample fields; canonical
output uses marker FEFF and version 0100. Builder output is reparsed before return.

Read limits default to 64 MiB of complete input and sixteen mebisamples. Sample
creation separately bounds padded samples and raw encoded bytes; standalone writes
default to a 64 MiB complete-output limit. Invalid envelopes, truncation, impossible
word counts, unsupported encodings, invalid ADPCM indices, zero sample rates, and
invalid active loops fail explicitly. The corpus gate checks all 373 standalone
waves, 9,962,188 sample values under the explicit signed-16 clipping policy,
metadata, exact preservation, and canonical output. DS saturation behavior has
separate sample-vector coverage. SWAR/SDAT archive APIs, STRM block streams, WAV
interoperability, and playback are not implied by this standalone-wave API.

## BMG messages

`BmgMessageBundle` provides a conservative, read-only view of standard `MESGbmg1` message resources. It supports little- and big-endian bundles, Windows-1252, UTF-16, Shift JIS, and UTF-8 declarations, variable-length INF1 metadata, and arbitrary auxiliary sections. Text spans and length-prefixed controls remain separate `BmgMessagePart` values, so decoding visible text never destroys embedded control types or payloads.

```csharp
using NdsForge.Nitro.Text;

BmgMessageBundle messages = BmgMessageBundle.Parse(File.ReadAllBytes("dialog.bmg"));
foreach (BmgMessage message in messages.Messages)
    Console.WriteLine(message.GetText());
```

UTF-16, UTF-8, and Windows-1252 decoding have no external dependencies. Shift JIS bundles retain the same lossless raw parts but require the caller to pass an explicit `Encoding` to `GetText`; this keeps legacy code-page policy out of the dependency-free package. `WritePreserved` returns the complete original allocation, including padding. Two observed producer quirks are explicit rather than silently repaired: some bundles overstate the section count after reaching their declared file end, and some final FLI1 sections claim up to 31 bytes of absent alignment padding.

The private compatibility suite covers 657 direct bundles, 77,683 messages, and 156,175 control sequences across both byte orders, all four standard encodings, INF1 record sizes from 4 through 12 bytes, and INF1/DAT1/MID1/FLW1/FLI1 layouts. All exposed offsets, metadata, text spans, controls, auxiliary bytes, and preservation output are locked to a reviewed semantic digest.

## Bottom-up LZ

BLZ stores tokens in reverse order and may retain an arbitrary leading prefix verbatim. Programs commonly preserve at least the 0x4000-byte ARM9 secure area, while overlays may retain a much shorter prefix selected by the original compressor.

```csharp
using NdsForge.Nitro.Compression;

byte[] stored = File.ReadAllBytes("overlay.bin");
if (BlzCodec.TryInspect(stored, out BlzInfo info))
{
    byte[] runtime = BlzCodec.Decompress(stored, maximumDecodedLength: 16 * 1024 * 1024);
    Console.WriteLine($"{info.EncodedLength} -> {runtime.Length} bytes");
}
```

`TryInspect` validates the trailing size envelope without allocating the decoded output. `Decompress` additionally validates every backward token and lookback before accepting the stream. Always choose an application-specific output limit for untrusted uploads.

Compression uses a deterministic greedy matcher and returns `false` when a valid BLZ envelope would not reduce the input. The prefix argument is a minimum: the encoder may preserve more leading bytes when doing so produces a smaller stream.

```csharp
if (BlzCodec.TryCompress(runtime, out byte[] encoded, uncompressedPrefixLength: 0x4000))
{
    File.WriteAllBytes("arm9.blz", encoded);
}
```

The implementation is independent. Decoder output is locked to a payload-free aggregate hash covering all BLZ programs and overlays in the private corpus after byte-exact comparison with a compiled reference implementation. Encoder output is compared semantically because valid token streams are not unique: both implementations must decode each other's deterministic test vectors to identical source bytes.
