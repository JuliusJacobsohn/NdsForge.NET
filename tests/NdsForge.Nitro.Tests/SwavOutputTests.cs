using System.Security.Cryptography;
using NdsForge.Nitro.Audio;

namespace NdsForge.Nitro.Tests;

public sealed class SwavOutputTests
{
    [Theory]
    [InlineData(NitroWaveEncoding.Pcm8, 4, 132, "66BDFB1E95EA1A8BE431BE3CDA7758282A9EDC4DBE30B860290168437E244B08", "999D246681879476F9BCCFFF88B7958F88948720236342B7A91461EA8718AD7C")]
    [InlineData(NitroWaveEncoding.Pcm16, 2, 132, "C60F5CEDA5DE0AC17F925FA1360E78F2E8DC308334DD46BB6032A794BB19F655", "F430CE1DCB7931D8FF3CFD6FA2C84283555170E292DEB58642F93DEA1D2A6089")]
    [InlineData(NitroWaveEncoding.ImaAdpcm, 8, 136, "60ABE4FEB9D335E1F88130B95EC41EFD6FF98C1DB3AC7562862F14714801C746", "2F2FEFB6A147F8F93271AF2905314F0B79861D05E0E0F96F409D683055D7A2F9")]
    public void PaddedLoopedCreationMatchesCompleteFileAndSampleVectors(NitroWaveEncoding encoding, int loopStart, int count, string fileHash, string sampleHash)
    {
        short[] samples = Enumerable.Range(0, 131).Select(i => (short)((i * 997 % 60001) - 30000)).ToArray();
        NitroWave wave = NitroWave.Create(samples, encoding, 22050,
            new() { PadFinalWord = true, LoopStartSample = loopStart, EncodingOptions = new() { InitialStepIndex = 20 } });
        SwavFile file = SwavFile.Create(wave);
        Assert.Equal(fileHash, Convert.ToHexString(SHA256.HashData(file.WritePreserved())));
        Assert.Equal(sampleHash, NitroWaveCodecTests.Digest(file.Wave.Decode(new() { AdpcmClipping = NitroAdpcmClipping.Signed16 })));
        Assert.Equal(count, file.Wave.SampleCount);
        Assert.Equal(loopStart, file.Wave.LoopStartSample);
        Assert.Equal(count, file.Wave.LoopEndSample);
    }

    [Theory]
    [InlineData(NitroWaveEncoding.Pcm8, "25D533713A2F490E97C3AA0C7BF1D9736EF6B1FDD318ED425D1C9869B42D767E")]
    [InlineData(NitroWaveEncoding.Pcm16, "CA1E2F02FAF773B7709356A633D8E0115B87D9AF683E5E59A1D76FF8FC493812")]
    [InlineData(NitroWaveEncoding.ImaAdpcm, "9E94F413C0C2DC181C26C2C9A07E677B713AB020DAD1ECD1100DC408C183DACF")]
    public void CompleteStoredWordVectorsHaveExactStandaloneEnvelopes(NitroWaveEncoding encoding, string expected)
    {
        byte[] encoded = encoding switch
        {
            NitroWaveEncoding.Pcm8 => Convert.FromHexString("8A8E92969A9EA2A6A9ADB1B5B9BDC1C5C9CDD0D4D8DCE0E4E8ECF0F3F7FBFF03070B0F13171A1E22262A2E32363A3D4145494D5155595D6164686C70748D9195999DA1A5A9ADB1B4B8BCC0C4C8CCD0D4D7DBDFE3E7EBEFF3F7FBFE02060A0E12161A1E2125292D3135393D4145484C5054585C6064686B6F738D9195989CA0A4"),
            NitroWaveEncoding.Pcm16 => NitroWaveCodec.Encode(Enumerable.Range(0, 128).Select(i => (short)((i * 997 % 60001) - 30000)).ToArray(), NitroWaveEncoding.Pcm16),
            _ => Convert.FromHexString("E8031400FFFFFFAF0000000001111012212232333433443224333443333443333433F4FFBF800080000000100010011111122223433243333443333443334324F3FFBF80"),
        };
        SwavFile file = SwavFile.Create(NitroWave.CreateEncoded(encoded, encoding, 22050, 759, 1, 1));
        byte[] bytes = file.WritePreserved();
        Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(bytes)));
        Assert.Equal(bytes, SwavFile.Parse(bytes).CreateBuilder().Build(new() { PreserveSourceLayout = false }));
    }
}
