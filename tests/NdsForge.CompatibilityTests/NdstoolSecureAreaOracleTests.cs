using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace NdsForge.CompatibilityTests;

public sealed partial class NdstoolSecureAreaOracleTests
{
    [Fact]
    public async Task EncryptionIsByteEqualToNdstool1503()
    {
        (string ndstool, string source) = GetInputs();
        byte[] tableBytes = await ReadExternalKeyTableAsync(source).ConfigureAwait(true);
        byte[] image = CreateDecryptedImage();
        byte[] plainArea = image.AsSpan(NdsSecureArea.Offset, NdsSecureArea.ByteLength).ToArray();
        byte[] actual = NdsSecureArea.Encrypt(plainArea, "SA01", new(tableBytes));
        string path = Path.Combine(Path.GetTempPath(), $"ndsforge-secure-oracle-{Guid.NewGuid():N}.nds");
        try
        {
            await File.WriteAllBytesAsync(path, image, TestContext.Current.CancellationToken).ConfigureAwait(true);
            await RunAsync(ndstool, path).ConfigureAwait(true);
            byte[] oracleImage = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true);
            byte[] expected = oracleImage.AsSpan(NdsSecureArea.Offset, NdsSecureArea.ByteLength).ToArray();

            Assert.True(expected.SequenceEqual(actual), DescribeDifference(expected, actual));
            Assert.Equal(plainArea, NdsSecureArea.Decrypt(expected, "SA01", new(tableBytes)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static (string Ndstool, string Source) GetInputs()
    {
        string? ndstool = Environment.GetEnvironmentVariable("NDSFORGE_NDSTOOL");
        string? source = Environment.GetEnvironmentVariable("NDSFORGE_NDSTOOL_SOURCE");
        if (string.IsNullOrWhiteSpace(ndstool) || string.IsNullOrWhiteSpace(source))
        {
            Assert.Skip("Set NDSFORGE_NDSTOOL and NDSFORGE_NDSTOOL_SOURCE to run the secure-area differential test.");
        }

        Assert.True(File.Exists(ndstool), $"ndstool oracle does not exist: {ndstool}");
        Assert.True(File.Exists(source), $"ndstool source does not exist: {source}");
        return (ndstool, source);
    }

    private static async Task<byte[]> ReadExternalKeyTableAsync(string path)
    {
        string source = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true);
        int start = source.IndexOf("encr_data[]", StringComparison.Ordinal);
        int end = source.IndexOf("};", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "The external ndstool source has no recognizable encr_data initializer.");
        byte[] bytes = ByteLiteral().Matches(source[start..end])
            .Select(static value => Convert.ToByte(value.Groups[1].Value, 16))
            .ToArray();
        Assert.Equal(NdsKey1KeyTable.ByteLength, bytes.Length);
        return bytes;
    }

    private static byte[] CreateDecryptedImage()
    {
        var image = new byte[0x8000];
        Encoding.ASCII.GetBytes("SECURE TEST").CopyTo(image, 0);
        Encoding.ASCII.GetBytes("SA01").CopyTo(image, 0x0C);
        Encoding.ASCII.GetBytes("HB").CopyTo(image, 0x10);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(0x20), NdsSecureArea.Offset);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(0x2C), NdsSecureArea.ByteLength);
        for (int index = NdsSecureArea.Offset + 8; index < image.Length; index++)
        {
            image[index] = (byte)((index * 29) ^ (index >> 3));
        }

        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(NdsSecureArea.Offset), 0xE7FFDEFF);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(NdsSecureArea.Offset + 4), 0xE7FFDEFF);
        return image;
    }

    private static async Task RunAsync(string ndstool, string image)
    {
        var start = new ProcessStartInfo(ndstool)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-se");
        start.ArgumentList.Add(image);
        using var process = new Process { StartInfo = start };
        Assert.True(process.Start(), "Failed to start the ndstool secure-area oracle.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.True(
            process.ExitCode == 0,
            $"ndstool exited with {process.ExitCode}.{Environment.NewLine}{await output.ConfigureAwait(true)}{Environment.NewLine}{await error.ConfigureAwait(true)}");
    }

    private static string DescribeDifference(byte[] expected, byte[] actual)
    {
        int offset = Enumerable.Range(0, Math.Min(expected.Length, actual.Length))
            .FirstOrDefault(index => expected[index] != actual[index], -1);
        return offset < 0
            ? $"Lengths differ: ndstool={expected.Length}, NdsForge={actual.Length}."
            : $"First difference at secure-area offset 0x{offset:X}: ndstool=0x{expected[offset]:X2}, NdsForge=0x{actual[offset]:X2}.";
    }

    [GeneratedRegex(@"0x([0-9A-Fa-f]{2})")]
    private static partial Regex ByteLiteral();
}
