using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using NdsForge.Nitro.Audio;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Verifies all standalone waves against neutral metadata, sample, and exact preservation expectations.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusSwavTests
{
    [Fact]
    [Trait("CorpusTier", "Full")]
    public async Task EveryStandaloneWaveMatchesSamplesAndRebuildsExactly()
    {
        var records = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var encodings = new Dictionary<NitroWaveEncoding, int>();
        byte[] signature = new byte[4];
        int sourceCount = 0;
        long samples = 0;
        foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries)
        {
            using NdsImage image = await NdsImage.OpenAsync(CorpusExpectations.Resolve(entry),
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            bool found = false;
            foreach (NdsFileAllocation allocation in image.FileSystem.Allocations)
            {
                using Stream stream = image.OpenRead(allocation.Data);
                if (stream.Read(signature) != 4 || !signature.AsSpan().SequenceEqual("SWAV"u8)) { continue; }
                stream.Position = 0;
                byte[] bytes = new byte[allocation.Data.Length];
                stream.ReadExactly(bytes);
                SwavFile file = SwavFile.Parse(bytes);
                Assert.Equal(bytes, file.WritePreserved());
                Assert.Equal(bytes, file.CreateBuilder().Build());
                Assert.Equal(bytes, file.CreateBuilder().Build(new() { PreserveSourceLayout = false }));
                Assert.Equal(bytes.Length, file.DeclaredLength);
                Assert.Equal(16, file.HeaderLength);
                NitroWave wave = file.Wave;
                Assert.Equal(wave.WriteSampleBlock(), NitroWave.ParseSampleBlock(wave.WriteSampleBlock()).WriteSampleBlock());
                short[] decoded = wave.Decode(new() { AdpcmClipping = NitroAdpcmClipping.Signed16 });
                Assert.Equal(wave.SampleCount, decoded.Length);
                byte[] sampleBytes = new byte[decoded.Length * 2];
                for (int i = 0; i < decoded.Length; i++) { BinaryPrimitives.WriteInt16LittleEndian(sampleBytes.AsSpan(i * 2), decoded[i]); }
                CorpusWavChecks.Check(file, sampleBytes);
                using var frame = new MemoryStream();
                using var writer = new BinaryWriter(frame, Encoding.UTF8, true);
                byte[] identity = SHA256.HashData(bytes);
                writer.Write(identity);
                writer.Write((int)wave.Encoding);
                writer.Write((int)wave.RawLoopFlag);
                writer.Write((int)wave.SampleRate);
                writer.Write((int)wave.Timer);
                writer.Write((int)wave.LoopStartWords);
                writer.Write((int)wave.RemainingWords);
                writer.Write(wave.EncodedData.Length);
                writer.Write(SHA256.HashData(wave.EncodedData.Span));
                writer.Write(SHA256.HashData(sampleBytes));
                writer.Flush();
                Assert.True(records.TryAdd(Convert.ToHexString(identity), frame.ToArray()));
                encodings[wave.Encoding] = encodings.GetValueOrDefault(wave.Encoding) + 1;
                samples += decoded.Length;
                found = true;
            }
            if (found) { sourceCount++; }
        }
        Assert.Equal(373, records.Count);
        Assert.Equal(3, sourceCount);
        Assert.Equal(143, encodings[NitroWaveEncoding.Pcm8]);
        Assert.Equal(8, encodings[NitroWaveEncoding.Pcm16]);
        Assert.Equal(222, encodings[NitroWaveEncoding.ImaAdpcm]);
        Assert.Equal(9962188, samples);
        using var combined = new MemoryStream();
        foreach (byte[] record in records.Values) { combined.Write(record); }
        Assert.Equal(46252, combined.Length);
        CorpusExpectations.AssertDigest("2F5F89CA47622CD91FF4B5B741F171F6C0C67D293C350BDD4FD27766310D1D5E",
            Convert.ToHexString(SHA256.HashData(combined.ToArray())));
    }
}
