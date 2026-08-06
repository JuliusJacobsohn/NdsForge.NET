namespace NdsForge.Tests;

public sealed class NdsModcryptTests
{
    [Fact]
    public void MatchesFixedAesCtrVectorWithDsiRegisterOrdering()
    {
        byte[] key = Convert.FromHexString("2B7E151628AED2A6ABF7158809CF4F3C");
        byte[] counter = Convert.FromHexString("F0F1F2F3F4F5F6F7F8F9FAFBFCFDFEFF");
        byte[] plaintext = Convert.FromHexString(
            "6BC1BEE22E409F96E93D7E117393172A" +
            "AE2D8A571E03AC9C9EB76FAC45AF8E51");
        byte[] expected = Convert.FromHexString(
            "E8DB2247BF2284BCD9B3412DC12D3B25" +
            "A6AE5E90EAA4C2090C6C6FF02D7D06E0");

        byte[] encrypted = NdsModcrypt.Transform(plaintext, key, counter);

        Assert.Equal(expected, encrypted);
        Assert.Equal(plaintext, NdsModcrypt.Transform(encrypted, key, counter));
    }

    [Fact]
    public void UnalignedSliceMatchesEquivalentWholeAreaBytes()
    {
        byte[] key = Enumerable.Range(0, 16).Select(static value => (byte)(value * 11)).ToArray();
        byte[] counter = Enumerable.Range(0, 16).Select(static value => (byte)(255 - value)).ToArray();
        byte[] plaintext = Enumerable.Range(0, 137).Select(static value => (byte)(value * 29)).ToArray();
        byte[] complete = NdsModcrypt.Transform(plaintext, key, counter);

        byte[] slice = NdsModcrypt.Transform(plaintext.AsSpan(23, 91), key, counter, byteOffset: 23);

        Assert.Equal(complete.AsSpan(23, 91).ToArray(), slice);
    }

    [Fact]
    public void HeaderContextUsesPublicKeyAndHmacCountersWhenFlagsRequestIt()
    {
        byte[] bytes = SyntheticImage.CreateDsiEnhanced();
        bytes[0x1C] |= 0x04;
        Enumerable.Range(0, 20).Select(static value => (byte)(0x40 + value)).ToArray().CopyTo(bytes, 0x300);
        Enumerable.Range(0, 20).Select(static value => (byte)(0x80 + value)).ToArray().CopyTo(bytes, 0x314);
        using NdsImage image = NdsImage.Load(bytes);

        NdsModcryptContext context = NdsModcryptContext.FromHeader(image.Header);

        Assert.True(image.Header.Dsi!.UsesInsecureModcryptKey);
        Assert.Equal(NdsModcryptKeyMode.InsecureHeaderKey, context.KeyMode);
        Assert.Equal(bytes.AsSpan(0, 16).ToArray(), context.ExportKey().ToArray());
        Assert.Equal(bytes.AsSpan(0x300, 16).ToArray(), context.ExportCounter(NdsModcryptArea.First).ToArray());
        Assert.Equal(bytes.AsSpan(0x314, 16).ToArray(), context.ExportCounter(NdsModcryptArea.Second).ToArray());
    }

    [Fact]
    public void SecureHeaderCanDeriveOrOverrideItsNormalKey()
    {
        byte[] bytes = SyntheticImage.CreateDsiEnhanced();
        bytes[0x1BF] &= 0x7F;
        using NdsImage image = NdsImage.Load(bytes);
        byte[] key = Enumerable.Repeat((byte)0xA7, 16).ToArray();

        NdsModcryptContext derived = NdsModcryptContext.FromHeader(image.Header);
        NdsModcryptContext context = NdsModcryptContext.FromHeader(image.Header, key);

        key[0] = 0;
        Assert.NotEqual(context.ExportKey().ToArray(), derived.ExportKey().ToArray());
        Assert.Equal(NdsModcryptKeyMode.SecureNormalKey, context.KeyMode);
        Assert.Equal(0xA7, context.ExportKey().Span[0]);
    }

    [Fact]
    public void KeyScramblerMatchesFixedRegisterOrderVectorAndGameCodeRecipe()
    {
        byte[] keyX = Convert.FromHexString("000102030405060708090A0B0C0D0E0F");
        byte[] keyY = Convert.FromHexString("F0E0D0C0B0A090807060504030201000");

        byte[] normalKey = NdsDsiKeyScrambler.DeriveNormalKey(keyX, keyY);

        Assert.Equal(Convert.FromHexString("D229A2743CA48188784FD4FAC742AFCD"), normalKey);
        Assert.Equal("NintendoTESTTSET"u8.ToArray(), NdsDsiKeyScrambler.CreateModcryptKeyX("TEST"));
    }

    [Fact]
    public void UndefinedAreasAndInvalidCryptographicWidthsAreRejected()
    {
        var context = new NdsModcryptContext(new byte[16], new byte[16], new byte[16]);

        Assert.Throws<ArgumentException>(() => NdsModcrypt.Transform([1], new byte[15], new byte[16]));
        Assert.Throws<ArgumentException>(() => new NdsModcryptContext(new byte[16], new byte[17], new byte[16]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NdsModcrypt.Transform([1], context, (NdsModcryptArea)99));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NdsModcrypt.Transform([1], new byte[16], new byte[16], -1));
    }

    [Fact]
    public async Task BoundedStreamTransformPreservesOwnershipAndTrailingInput()
    {
        byte[] input = Enumerable.Range(0, 100_007).Select(static value => (byte)(value * 13)).ToArray();
        var context = new NdsModcryptContext(
            Enumerable.Repeat((byte)0x31, 16).ToArray(),
            Enumerable.Repeat((byte)0x52, 16).ToArray(),
            new byte[16]);
        using var source = new MemoryStream(input);
        using var destination = new MemoryStream();

        await NdsModcrypt.TransformAsync(
            source,
            destination,
            100_000,
            context,
            NdsModcryptArea.First,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(100_000, source.Position);
        Assert.Equal(100_000, destination.Length);
        Assert.True(source.CanRead);
        Assert.True(destination.CanWrite);
        Assert.Equal(
            NdsModcrypt.Transform(input.AsSpan(0, 100_000), context, NdsModcryptArea.First),
            destination.ToArray());
    }

    [Fact]
    public async Task ImageConvenienceTransformsExactlyTheDeclaredArea()
    {
        byte[] bytes = SyntheticImage.CreateDsiEnhanced();
        bytes[0x1C] |= 0x04;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x220), 0x1200);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x224), 113);
        for (int index = 0; index < 113; index++)
        {
            bytes[0x1200 + index] = (byte)(index * 17);
        }

        using NdsImage image = NdsImage.Load(bytes);
        NdsModcryptContext context = NdsModcryptContext.FromHeader(image.Header);
        using var destination = new MemoryStream();

        await image.TransformModcryptAreaAsync(
            NdsModcryptArea.First,
            destination,
            context,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(
            NdsModcrypt.Transform(bytes.AsSpan(0x1200, 113), context, NdsModcryptArea.First),
            destination.ToArray());
    }

    [Fact]
    public async Task TruncatedFirstChunkDoesNotEmitUninitializedOrPartialOutput()
    {
        var context = new NdsModcryptContext(new byte[16], new byte[16], new byte[16]);
        using var source = new MemoryStream(new byte[31]);
        using var destination = new MemoryStream();

        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
            await NdsModcrypt.TransformAsync(
                source,
                destination,
                32,
                context,
                NdsModcryptArea.First,
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));

        Assert.Empty(destination.ToArray());
        Assert.True(source.CanRead);
        Assert.True(destination.CanWrite);
    }
}
