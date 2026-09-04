using System.Numerics;
using System.Security.Cryptography;

namespace NdsForge.Tests;

public sealed class NdsRsaEncodingTests
{
    private const string ModulusHex =
        "E7FDCC8D3BF7888D17DAFC0EF57B104B2E715E9CDAF18BC8271E003BA2E37A1D9" +
        "44BADCF7CCE9317F39EE95249AA629380181EEE7DEF786DEC16754C06BBB59BB0412" +
        "3F1AF322758CA3F60717E76478389EB11887F710FB5FD2F4BB41611411C0827B58C" +
        "F94F1F7D4C79D0800BAA826E56238947C4643837D4389E6FC3869261";

    private const string RawSignatureHex =
        "CC1E6684949C04D66B05A8A50CB492C467EDC92C46E585936C51876A5129CA4F1C" +
        "1440F4276025E0B2A3960B160F120713E032373AC297C0593BB7E7A8C3E09E96C4" +
        "F4AAE2E589C2DABCBE6D327D45FCEA326E6B5E42B01F8D957A9C8577F3558E0A" +
        "70C71DB0CC14DC70337ACDD11AAD58CBDF6D1748EBD7A1F1683F438F8B41";

    private const string IdentifiedSignatureHex =
        "92D3803876FC179FD6BA16E407A395CA84923FEF5FAD72A8F00A00C7F2B4D4AFC" +
        "F81B1643C8E27CBFB8D2FB37691B4316EDF6EE588DF767E86CF056C9BA4CDEB793" +
        "168568E00D5FAB657DE543D38562C92DB6037A8D098F35CBCC4C45698E0850454A" +
        "923EF19AFD861F0AF8E51E06411501E8D3A509B2B254680DDF20818330A";

    [Fact]
    public void AcceptsNativeRawDigestSignatureAndRejectsAsn1DigestInfo()
    {
        var key = new NdsDsiRsaPublicKey(Convert.FromHexString(ModulusHex), [1, 0, 1]);
        byte[] header = CreateHeader();

        Assert.True(key.VerifyHeader(header, Convert.FromHexString(RawSignatureHex)));
        Assert.False(key.VerifyHeader(header, Convert.FromHexString(IdentifiedSignatureHex)));
        header[0x100] ^= 1;
        Assert.False(key.VerifyHeader(header, Convert.FromHexString(RawSignatureHex)));
    }

    [Fact]
    public void RejectsSignatureOutsideModulusAndInvalidWidths()
    {
        byte[] modulus = Convert.FromHexString(ModulusHex);
        var key = new NdsDsiRsaPublicKey(modulus, [1, 0, 1]);
        byte[] header = CreateHeader();

        Assert.False(key.VerifyHeader(header, modulus));
        Assert.False(key.VerifyHeader(header, new byte[128]));
        Assert.False(key.VerifyHeader(header, Enumerable.Repeat((byte)0xFF, 128).ToArray()));
        Assert.Throws<ArgumentException>(() => key.VerifyHeader(header.AsSpan(1), new byte[128]));
        Assert.Throws<ArgumentException>(() => key.VerifyHeader(header, new byte[127]));
    }

    [Fact]
    public void SigningProducesExactNativePaddingWithDeterministicOutput()
    {
        using RSA rsa = RSA.Create(1024);
        using var signer = new NdsDsiRsaSignatureProvider(rsa);
        byte[] header = CreateHeader();
        byte[] first = new byte[128];
        byte[] second = new byte[128];
        signer.SignHeader(header, first);
        signer.SignHeader(header, second);
        Assert.Equal(first, second);

        RSAParameters publicKey = rsa.ExportParameters(includePrivateParameters: false);
        var signed = new BigInteger(first, isUnsigned: true, isBigEndian: true);
        var exponent = new BigInteger(publicKey.Exponent!, isUnsigned: true, isBigEndian: true);
        var modulus = new BigInteger(publicKey.Modulus!, isUnsigned: true, isBigEndian: true);
        byte[] decoded = BigInteger.ModPow(signed, exponent, modulus).ToByteArray(isUnsigned: true, isBigEndian: true);
        Assert.Equal(127, decoded.Length);
        Assert.Equal(1, decoded[0]);
        Assert.All(decoded[1..106], static value => Assert.Equal(0xFF, value));
        Assert.Equal(0, decoded[106]);
        using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        digest.AppendData(header);
        Assert.Equal(digest.GetHashAndReset(), decoded[107..]);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    [InlineData(106, 0)]
    [InlineData(107, 255)]
    public void RejectsMalformedTypeOnePaddingEvenWithTheCorrectDigest(int offset, byte value)
    {
        using RSA rsa = RSA.Create(1024);
        RSAParameters parameters = rsa.ExportParameters(includePrivateParameters: true);
        byte[] header = CreateHeader();
        byte[] encoded = Enumerable.Repeat((byte)0xFF, 128).ToArray();
        encoded[0] = 0;
        encoded[1] = 1;
        encoded[107] = 0;
        using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        digest.AppendData(header);
        digest.GetHashAndReset().CopyTo(encoded, 108);
        encoded[offset] = value;
        var message = new BigInteger(encoded, isUnsigned: true, isBigEndian: true);
        var privateExponent = new BigInteger(parameters.D!, isUnsigned: true, isBigEndian: true);
        var modulus = new BigInteger(parameters.Modulus!, isUnsigned: true, isBigEndian: true);
        byte[] number = BigInteger.ModPow(message, privateExponent, modulus).ToByteArray(isUnsigned: true, isBigEndian: true);
        byte[] signature = new byte[128];
        number.CopyTo(signature, 128 - number.Length);

        Assert.False(NdsDsiRsaPublicKey.FromRsa(rsa).VerifyHeader(header, signature));
    }

    [Fact]
    public void ValidatesPublicParametersAndCopiesCallerBuffers()
    {
        byte[] modulus = Convert.FromHexString(ModulusHex);
        byte[] exponent = [1, 0, 1];
        var key = new NdsDsiRsaPublicKey(modulus, exponent);
        modulus[0] = 0;
        exponent[0] = 0;
        Assert.True(key.VerifyHeader(CreateHeader(), Convert.FromHexString(RawSignatureHex)));
        Assert.Throws<ArgumentException>(() => new NdsDsiRsaPublicKey(modulus, [1, 0, 1]));
        Assert.Throws<ArgumentException>(() => new NdsDsiRsaPublicKey(new byte[127], [1, 0, 1]));
        Assert.Throws<ArgumentException>(() => new NdsDsiRsaPublicKey(Convert.FromHexString(ModulusHex), []));
        Assert.Throws<ArgumentException>(() => new NdsDsiRsaPublicKey(Convert.FromHexString(ModulusHex), [0]));
        Assert.Throws<ArgumentException>(() => new NdsDsiRsaPublicKey(Convert.FromHexString(ModulusHex), [1]));
        Assert.Throws<ArgumentException>(() => new NdsDsiRsaPublicKey(Convert.FromHexString(ModulusHex), [2]));
        Assert.Throws<ArgumentException>(() => new NdsDsiRsaPublicKey(Convert.FromHexString(ModulusHex), new byte[129]));
    }

    [Fact]
    public void SigningSnapshotSurvivesSourceDisposalAndRejectsInvalidOutputWithoutMutation()
    {
        using RSA rsa = RSA.Create(1024);
        using var signer = new NdsDsiRsaSignatureProvider(rsa);
        NdsDsiRsaPublicKey key = NdsDsiRsaPublicKey.FromRsa(rsa);
        rsa.Dispose();
        byte[] output = Enumerable.Repeat((byte)0xA5, 128).ToArray();
        Assert.Throws<ArgumentException>(() => signer.SignHeader(new byte[0xDFF], output));
        Assert.All(output, static value => Assert.Equal(0xA5, value));
        Assert.Throws<ArgumentException>(() => signer.SignHeader(CreateHeader(), output.AsSpan(1)));
        Assert.All(output, static value => Assert.Equal(0xA5, value));
        signer.SignHeader(CreateHeader(), output);
        Assert.True(key.VerifyHeader(CreateHeader(), output));
        signer.Dispose();
        signer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => signer.SignHeader(CreateHeader(), output));
    }

    private static byte[] CreateHeader() => Enumerable.Range(0, 0xE00)
        .Select(static index => unchecked((byte)((index * 91) ^ (index >> 7) ^ 91))).ToArray();
}
