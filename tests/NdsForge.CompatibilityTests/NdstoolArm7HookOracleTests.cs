using System.Diagnostics;
using System.Globalization;

namespace NdsForge.CompatibilityTests;

public sealed class NdstoolArm7HookOracleTests
{
    [Fact]
    public async Task AlignedHookIsByteEqualToNdstool1503()
    {
        string ndstool = GetNdstoolPath();
        string root = Path.Combine(Path.GetTempPath(), $"ndsforge-hook-oracle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            byte[] source = await BuildImageAsync().ConfigureAwait(true);
            byte[] hook = [0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80];
            string oraclePath = Path.Combine(root, "oracle.nds");
            string hookPath = Path.Combine(root, "hook.bin");
            await File.WriteAllBytesAsync(oraclePath, source, TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllBytesAsync(hookPath, hook, TestContext.Current.CancellationToken).ConfigureAwait(true);

            await RunHookAsync(ndstool, oraclePath, hookPath).ConfigureAwait(true);
            byte[] expected = await File.ReadAllBytesAsync(oraclePath, TestContext.Current.CancellationToken).ConfigureAwait(true);
            byte[] actual = NdsLegacyArm7Hook.Apply(source, hook).Image.ToArray();

            Assert.True(expected.SequenceEqual(actual), DescribeDifference(expected, actual));
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
            Assert.Skip("Set NDSFORGE_NDSTOOL to run the differential ARM7 hook test.");
        }

        return path;
    }

    private static ValueTask<byte[]> BuildImageAsync()
    {
        var builder = new NdsImageBuilder
        {
            Title = "HOOK ORACLE",
            GameCode = "HK01",
            MakerCode = "HB",
            Arm9 = new(NdsProcessor.Arm9, [0xA9, 1, 2, 3], 0x0200_0000, 0x0200_0000),
            Arm7 = new(NdsProcessor.Arm7, [0xA7, 4, 5, 6], 0x0238_0000, 0x0238_0000),
        };
        builder.FileSystem.AddFile("/data.bin", [7, 8, 9]);
        return builder.BuildAsync(
            new() { Profile = NdsImageBuildProfile.Ndstool1503 },
            TestContext.Current.CancellationToken);
    }

    private static async Task RunHookAsync(string ndstool, string image, string hook)
    {
        var start = new ProcessStartInfo(ndstool)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in new[] { "-k", image, "-7", hook })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.True(process.ExitCode == 0, $"{await stdout.ConfigureAwait(true)}\n{await stderr.ConfigureAwait(true)}");
    }

    private static string DescribeDifference(byte[] expected, byte[] actual)
    {
        int comparable = Math.Min(expected.Length, actual.Length);
        int[] offsets = Enumerable.Range(0, comparable)
            .Where(index => expected[index] != actual[index])
            .Take(24)
            .ToArray();
        return $"Lengths: {expected.Length:X}/{actual.Length:X}; first differences: " +
            string.Join(", ", offsets.Select(offset => offset.ToString("X", CultureInfo.InvariantCulture)));
    }
}
