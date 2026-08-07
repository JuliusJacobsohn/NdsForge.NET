using System.Collections.ObjectModel;
using System.Diagnostics;

namespace NdsForge.CompatibilityTests;

public sealed class NdstoolOracleTests
{
    [Fact]
    public async Task ExtractionIsByteEqualToNdstoolWhereDefined()
    {
        (string romPath, string ndstoolPath) = GetInputs();
        string root = Path.Combine(Path.GetTempPath(), $"ndsforge-oracle-{Guid.NewGuid():N}");
        string oracle = Path.Combine(root, "oracle");
        string actual = Path.Combine(root, "actual");
        Directory.CreateDirectory(oracle);
        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            await RunNdstoolAsync(ndstoolPath, romPath, oracle, cancellationToken).ConfigureAwait(true);
            using NdsImage image = await NdsImage.OpenAsync(
                romPath,
                cancellationToken: cancellationToken).ConfigureAwait(true);
            await image.ExtractAsync(actual, cancellationToken: cancellationToken).ConfigureAwait(true);

            await AssertEqualAsync(oracle, "header.bin", actual, "header.bin", cancellationToken).ConfigureAwait(true);
            await AssertEqualAsync(oracle, "logo.bin", actual, "logo.bin", cancellationToken).ConfigureAwait(true);
            await AssertEqualAsync(oracle, "arm9.bin", actual, "arm9.bin", cancellationToken).ConfigureAwait(true);
            await AssertEqualAsync(oracle, "arm7.bin", actual, "arm7.bin", cancellationToken).ConfigureAwait(true);
            await AssertEqualAsync(oracle, "banner.bin", actual, "banner.bin", cancellationToken).ConfigureAwait(true);
            await AssertEqualAsync(oracle, "y9.bin", actual, "tables/arm9-overlays.bin", cancellationToken).ConfigureAwait(true);
            await AssertEqualAsync(oracle, "y7.bin", actual, "tables/arm7-overlays.bin", cancellationToken).ConfigureAwait(true);
            await AssertDirectoryEqualAsync(
                Path.Combine(oracle, "data"),
                Path.Combine(actual, "data"),
                cancellationToken).ConfigureAwait(true);

            foreach (NdsOverlay overlay in image.Arm9Overlays.Concat(image.Arm7Overlays))
            {
                if (overlay.Id != overlay.FileId)
                {
                    continue;
                }

                string expected = FormattableString.Invariant($"overlay/overlay_{overlay.Id:D4}.bin");
                string processor = overlay.Processor == NdsProcessor.Arm9 ? "arm9" : "arm7";
                string observed = FormattableString.Invariant(
                    $"overlays/{processor}/overlay_{overlay.Id:D4}_file_{overlay.FileId:D5}.bin");
                await AssertEqualAsync(oracle, expected, actual, observed, cancellationToken).ConfigureAwait(true);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static (string RomPath, string NdstoolPath) GetInputs()
    {
        string? romPath = Environment.GetEnvironmentVariable("NDSFORGE_TEST_ROM");
        string? ndstoolPath = Environment.GetEnvironmentVariable("NDSFORGE_NDSTOOL");
        if (string.IsNullOrWhiteSpace(romPath) || string.IsNullOrWhiteSpace(ndstoolPath))
        {
            Assert.Skip("Set NDSFORGE_TEST_ROM and NDSFORGE_NDSTOOL to run differential extraction tests.");
        }

        Assert.True(File.Exists(romPath), $"Private fixture does not exist: {romPath}");
        Assert.True(File.Exists(ndstoolPath), $"ndstool oracle does not exist: {ndstoolPath}");
        return (romPath, ndstoolPath);
    }

    private static async Task RunNdstoolAsync(
        string ndstoolPath,
        string romPath,
        string output,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(ndstoolPath)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        AddArguments(startInfo.ArgumentList, romPath, output);
        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start(), "Failed to start ndstool.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(true);
        string outputText = await standardOutput.ConfigureAwait(true);
        string errorText = await standardError.ConfigureAwait(true);
        Assert.True(
            process.ExitCode == 0,
            $"ndstool exited with {process.ExitCode}.{Environment.NewLine}{outputText}{Environment.NewLine}{errorText}");
    }

    private static void AddArguments(Collection<string> arguments, string romPath, string output)
    {
        arguments.Add("-x");
        arguments.Add(romPath);
        AddOutput("-9", "arm9.bin");
        AddOutput("-7", "arm7.bin");
        AddOutput("-y9", "y9.bin");
        AddOutput("-y7", "y7.bin");
        AddOutput("-d", "data");
        AddOutput("-y", "overlay");
        AddOutput("-t", "banner.bin");
        AddOutput("-h", "header.bin");
        AddOutput("-o", "logo.bin");

        void AddOutput(string option, string relativePath)
        {
            arguments.Add(option);
            arguments.Add(Path.Combine(output, relativePath));
        }
    }

    private static async Task AssertDirectoryEqualAsync(
        string expectedRoot,
        string actualRoot,
        CancellationToken cancellationToken)
    {
        string[] expectedFiles = Directory.GetFiles(expectedRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(expectedRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] actualFiles = Directory.GetFiles(actualRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(actualRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedFiles, actualFiles);
        foreach (string relativePath in expectedFiles)
        {
            await AssertFileEqualAsync(
                Path.Combine(expectedRoot, relativePath),
                Path.Combine(actualRoot, relativePath),
                cancellationToken).ConfigureAwait(true);
        }
    }

    private static Task AssertEqualAsync(
        string expectedRoot,
        string expectedRelativePath,
        string actualRoot,
        string actualRelativePath,
        CancellationToken cancellationToken) => AssertFileEqualAsync(
            Path.Combine(expectedRoot, expectedRelativePath),
            Path.Combine(actualRoot, actualRelativePath),
            cancellationToken);

    private static async Task AssertFileEqualAsync(
        string expectedPath,
        string actualPath,
        CancellationToken cancellationToken)
    {
        Assert.True(File.Exists(expectedPath), $"Oracle did not create {expectedPath}");
        Assert.True(File.Exists(actualPath), $"NdsForge did not create {actualPath}");
        var expectedInfo = new FileInfo(expectedPath);
        var actualInfo = new FileInfo(actualPath);
        Assert.Equal(expectedInfo.Length, actualInfo.Length);
        using FileStream expected = File.OpenRead(expectedPath);
        using FileStream actual = File.OpenRead(actualPath);
        byte[] expectedBuffer = new byte[64 * 1024];
        byte[] actualBuffer = new byte[64 * 1024];
        long offset = 0;
        while (true)
        {
            int expectedCount = await expected.ReadAsync(expectedBuffer, cancellationToken).ConfigureAwait(true);
            int actualCount = await actual.ReadAsync(actualBuffer, cancellationToken).ConfigureAwait(true);
            Assert.True(expectedCount == actualCount, $"Read lengths differ at offset 0x{offset:X} for {actualPath}.");
            if (expectedCount == 0)
            {
                return;
            }

            Assert.True(
                expectedBuffer.AsSpan(0, expectedCount).SequenceEqual(actualBuffer.AsSpan(0, actualCount)),
                $"Bytes differ at or after offset 0x{offset:X} for {actualPath}.");
            offset += expectedCount;
        }
    }
}
