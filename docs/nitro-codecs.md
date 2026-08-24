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
