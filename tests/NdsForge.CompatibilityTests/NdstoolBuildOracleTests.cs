using System.Diagnostics;

namespace NdsForge.CompatibilityTests;

public sealed class NdstoolBuildOracleTests
{
    [Fact]
    public async Task MinimalBuildIsByteEqualToNdstool1503()
    {
        string ndstool = GetNdstoolPath();
        string root = Path.Combine(Path.GetTempPath(), $"ndsforge-build-oracle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string dataDirectory = Path.Combine(root, "data");
            Directory.CreateDirectory(Path.Combine(dataDirectory, "nested"));
            string arm9 = Path.Combine(root, "arm9.bin");
            string arm7 = Path.Combine(root, "arm7.bin");
            string logo = Path.Combine(root, "logo.bin");
            string expected = Path.Combine(root, "ndstool.nds");
            string actual = Path.Combine(root, "ndsforge.nds");
            await File.WriteAllBytesAsync(arm9, [0xA9, 1, 2], TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllBytesAsync(arm7, [0xA7, 3], TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllBytesAsync(logo, new byte[156], TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllBytesAsync(
                Path.Combine(dataDirectory, "nested", "file.bin"),
                [4, 5, 6],
                TestContext.Current.CancellationToken).ConfigureAwait(true);

            await RunAsync(ndstool, expected, arm9, arm7, logo, dataDirectory).ConfigureAwait(true);
            var builder = new NdsImageBuilder
            {
                Title = "BUILD TEST",
                GameCode = "BT01",
                MakerCode = "HB",
                Arm9 = new(NdsProcessor.Arm9, [0xA9, 1, 2], 0x02000000, 0x02000000),
                Arm7 = new(NdsProcessor.Arm7, [0xA7, 3], 0x02380000, 0x02380000),
            };
            builder.SetNintendoLogo(new byte[156]);
            builder.FileSystem.AddFile("/nested/file.bin", [4, 5, 6]);
            await builder.WriteAsync(
                actual,
                new() { Profile = NdsImageBuildProfile.Ndstool1503 },
                TestContext.Current.CancellationToken).ConfigureAwait(true);

            byte[] expectedBytes = await File.ReadAllBytesAsync(expected, TestContext.Current.CancellationToken).ConfigureAwait(true);
            byte[] actualBytes = await File.ReadAllBytesAsync(actual, TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.Equal(expectedBytes, actualBytes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string GetNdstoolPath()
    {
        string? path = Environment.GetEnvironmentVariable("NDSFORGE_NDSTOOL");
        if (string.IsNullOrWhiteSpace(path))
        {
            Assert.Skip("Set NDSFORGE_NDSTOOL to run differential build tests.");
        }

        return path;
    }

    private static async Task RunAsync(
        string ndstool,
        string output,
        string arm9,
        string arm7,
        string logo,
        string data)
    {
        var info = new ProcessStartInfo(ndstool)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in new[]
        {
            "-c", output, "-9", arm9, "-7", arm7, "-d", data, "-o", logo, "-h", "0x4000",
            "-g", "BT01", "HB", "BUILD TEST", "2",
            "-r9", "0x02000000", "-e9", "0x02000000",
            "-r7", "0x02380000", "-e7", "0x02380000",
            "-n", "0", "0",
        })
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.True(process.ExitCode == 0, $"{await stdout.ConfigureAwait(true)}\n{await stderr.ConfigureAwait(true)}");
    }
}
