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
