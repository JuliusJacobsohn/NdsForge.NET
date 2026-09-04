using System.Buffers.Binary;
using System.Security.Cryptography;
using NdsForge.Shared;

namespace NdsForge.Tests;

/// <summary>Owns synthetic credentials and a multi-layer authentication recipe without retaining any publisher material.</summary>
internal sealed class LateDsBuildFixture : IDisposable
{
    private readonly RSA _rsa = RSA.Create(1024);
    private readonly NdsDsiRsaSignatureProvider _signer;

    internal LateDsBuildFixture(bool compressedArm9 = false)
    {
        _signer = new(_rsa);
        PublicKey = NdsDsiRsaPublicKey.FromRsa(_rsa);
        SecureKey = new(Enumerable.Range(0, NdsKey1KeyTable.ByteLength).Select(static index => (byte)(index * 37 + 11)).ToArray());
        Policy = NdsDsIntegrityOptions.CreateHmacSha1(ProgramKey, BannerKey, SecureKey, _signer, PublicKey);
        byte[] arm9 = Enumerable.Repeat((byte)0xA5, 0x9000).ToArray();
        for (int index = 8; index < 0x4000; index++) { arm9[index] = (byte)((index * 29) ^ (index >> 3)); }
        Write(arm9, 0, 0xE7FFDEFF);
        Write(arm9, 4, 0xE7FFDEFF);
        BinaryPrimitives.WriteUInt16LittleEndian(arm9.AsSpan(0xE), NdsChecksums.ComputeCrc16(arm9.AsSpan(0x10, 0x7F0)));
        arm9.AsSpan(0x4000, 0x24).Clear();
        Write(arm9, 0x4018, 0x05057533);
        Write(arm9, 0x401C, 0xDEC00621);
        Write(arm9, 0x4020, 0x2106C0DE);
        ClassicKey.CopyTo(arm9, 0x5000);
        byte[] first = Enumerable.Repeat((byte)0x51, 513).ToArray();
        byte[] second = Enumerable.Repeat((byte)0x62, 1024).ToArray();
        using (IncrementalHash hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, ClassicKey))
        {
            hash.AppendData(first);
            hash.GetHashAndReset().CopyTo(arm9, 0x6000);
            hash.AppendData(second);
            hash.GetHashAndReset().CopyTo(arm9, 0x6014);
        }

        if (compressedArm9)
        {
            Assert.True(BlzEngine.TryCompress(arm9, out byte[] encoded, 0x4040));
            Write(encoded, 0x4014, checked(0x02000000u + (uint)encoded.Length));
            arm9 = encoded;
        }

        byte[] footer = new byte[12];
        Write(footer, 0, 0xDEC00621);
        Write(footer, 4, 0x4000);
        Write(footer, 8, 0x6000);
        byte[] extension = Enumerable.Range(0, 0xE80).Select(static index => (byte)(index * 17 + 3)).ToArray();
        var metadata = new NdsDsBuildMetadata().SetExtensionTemplate(extension);
        metadata.DsiFlags = 0xA0;
        metadata.ProgramFeatures = (NdsProgramFeatures)0xE9;
        metadata.Arm9ParametersRelativeOffset = 0x4000;
        metadata.Arm7ParametersRelativeOffset = 0x20;
        metadata.Integrity = Policy;
        Builder = new()
        {
            Title = "LATE DS TEST",
            GameCode = "TEST",
            Arm9 = new NdsProgramDefinition(NdsProcessor.Arm9, arm9, 0x02000000, 0x02000800).SetFooter(footer),
            Arm7 = new(NdsProcessor.Arm7, new byte[0x300], 0x03800000, 0x03800000),
            Banner = new NdsBannerBuilder().SetTitle(NdsBannerLanguage.English, "Signed fixture").Build(),
            DsMetadata = metadata,
            Arm9OverlayAuthentication = NdsOverlayAuthenticationBuildOptions.CreateHmacSha1(0x6000, ClassicKey,
                compressedArm9 ? NdsProgramStorageEncoding.Blz : NdsProgramStorageEncoding.Plain,
                compressedArm9 ? 0x4040 : 0),
        };
        Builder.AddOverlay(new(NdsProcessor.Arm9, 1, first, 0x02100000, 513, flags: 2));
        Builder.AddOverlay(new(NdsProcessor.Arm9, 2, second, 0x02200000, 1024, flags: 2));
        Builder.FileSystem.AddFile("/named.bin", "named payload"u8);
    }

    internal NdsImageBuilder Builder { get; }
    internal NdsDsIntegrityOptions Policy { get; }
    internal NdsDsiRsaPublicKey PublicKey { get; }
    internal NdsKey1KeyTable SecureKey { get; }
    internal byte[] ProgramKey { get; } = Enumerable.Range(0, 64).Select(static index => (byte)index).ToArray();
    internal byte[] BannerKey { get; } = Enumerable.Range(0, 64).Select(static index => (byte)(255 - index)).ToArray();
    internal byte[] ClassicKey { get; } = SyntheticImage.CreateOverlayAuthenticationKey();

    internal NdsValidationOptions Validation() => new NdsValidationOptions()
        .SetDsProgramHmacKey(ProgramKey).SetDsBannerHmacKey(BannerKey).SetDsRsaPublicKey(PublicKey)
        .SetSecureAreaKeyTable(SecureKey).SetArm9OverlayHmacKey(ClassicKey);

    public void Dispose()
    {
        _signer.Dispose();
        _rsa.Dispose();
    }

    private static void Write(Span<byte> data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(data[offset..], value);
}
