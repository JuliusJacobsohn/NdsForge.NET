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

`Lz10Codec`, `Lz11Codec`, and `RleCodec` also expose deterministic encoders. Huffman is currently decode-only. All decoders reject truncated tokens, invalid look-behinds or trees, blocks that cross the declared output, and caller-defined allocation limits. LZ10, LZ11, and eight-bit Huffman output has been compared byte-for-byte with an independently compiled implementation for every structurally valid candidate in the private ROM corpus. The corpus contains no genuine four-bit Huffman or run-length allocation, so those paths additionally use hand-authored grammar vectors and cross-decoding against the compiled reference encoder.

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

The private compatibility suite covers 6,762 valid NARC allocations and all 826,541 contained files. It verifies exact preservation, canonical rebuild/reparse semantics, and a payload-and-path aggregate digest locked to a reviewed compatibility baseline.

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
