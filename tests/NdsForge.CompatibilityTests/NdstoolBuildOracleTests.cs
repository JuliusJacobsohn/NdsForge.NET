using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;

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

            await RunAsync(ndstool, expected, arm9, arm7, logo, dataDirectory, banner: null).ConfigureAwait(true);
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
            Assert.True(expectedBytes.SequenceEqual(actualBytes), DescribeDifference(expectedBytes, actualBytes));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BannerAndMultiDirectoryBuildIsByteEqualToNdstool1503()
    {
        string ndstool = GetNdstoolPath();
        string root = Path.Combine(Path.GetTempPath(), $"ndsforge-build-oracle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string dataDirectory = Path.Combine(root, "data");
            Directory.CreateDirectory(Path.Combine(dataDirectory, "alpha"));
            Directory.CreateDirectory(Path.Combine(dataDirectory, "beta"));
            string arm9 = Path.Combine(root, "arm9.bin");
            string arm7 = Path.Combine(root, "arm7.bin");
            string logo = Path.Combine(root, "logo.bin");
            string bannerPath = Path.Combine(root, "banner.bin");
            string expected = Path.Combine(root, "ndstool.nds");
            string actual = Path.Combine(root, "ndsforge.nds");
            byte[] arm9Bytes = [0xA9, 1, 2, 3, 4];
            byte[] arm7Bytes = [0xA7, 5, 6, 7, 8, 9];
            NdsBanner banner = new NdsBannerBuilder()
                .SetTitle(NdsBannerLanguage.English, "Compatibility")
                .Build();
            await File.WriteAllBytesAsync(arm9, arm9Bytes, TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllBytesAsync(arm7, arm7Bytes, TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllBytesAsync(logo, new byte[156], TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllBytesAsync(bannerPath, banner.RawData, TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllBytesAsync(
                Path.Combine(dataDirectory, "root.bin"),
                [0x10],
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllBytesAsync(
                Path.Combine(dataDirectory, "alpha", "one.bin"),
                [0x20, 0x21],
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllBytesAsync(
                Path.Combine(dataDirectory, "alpha", "second.bin"),
                [0x22, 0x23, 0x24],
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllBytesAsync(
                Path.Combine(dataDirectory, "beta", "two.bin"),
                [0x30, 0x31, 0x32, 0x33, 0x34],
                TestContext.Current.CancellationToken).ConfigureAwait(true);

            await RunAsync(ndstool, expected, arm9, arm7, logo, dataDirectory, bannerPath).ConfigureAwait(true);
            var builder = new NdsImageBuilder
            {
                Title = "BUILD TEST",
                GameCode = "BT01",
                MakerCode = "HB",
                Arm9 = new(NdsProcessor.Arm9, arm9Bytes, 0x02000000, 0x02000000),
                Arm7 = new(NdsProcessor.Arm7, arm7Bytes, 0x02380000, 0x02380000),
                Banner = banner,
            };
            builder.SetNintendoLogo(new byte[156]);
            builder.FileSystem.AddFile("/root.bin", [0x10]);
            builder.FileSystem.AddFile("/alpha/one.bin", [0x20, 0x21]);
            builder.FileSystem.AddFile("/alpha/second.bin", [0x22, 0x23, 0x24]);
            builder.FileSystem.AddFile("/beta/two.bin", [0x30, 0x31, 0x32, 0x33, 0x34]);
            await builder.WriteAsync(
                actual,
                new() { Profile = NdsImageBuildProfile.Ndstool1503 },
                TestContext.Current.CancellationToken).ConfigureAwait(true);

            byte[] expectedBytes = await File.ReadAllBytesAsync(expected, TestContext.Current.CancellationToken).ConfigureAwait(true);
            byte[] actualBytes = await File.ReadAllBytesAsync(actual, TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.True(expectedBytes.SequenceEqual(actualBytes), DescribeDifference(expectedBytes, actualBytes));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PrivateArm9AndArm7OverlayBuildIsByteEqualToNdstool1503()
    {
        string ndstool = GetNdstoolPath();
        string root = Path.Combine(Path.GetTempPath(), $"ndsforge-build-oracle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string dataDirectory = Path.Combine(root, "data");
            string overlayDirectory = Path.Combine(root, "overlay");
            Directory.CreateDirectory(dataDirectory);
            Directory.CreateDirectory(overlayDirectory);
            string arm9 = Path.Combine(root, "arm9.bin");
            string arm7 = Path.Combine(root, "arm7.bin");
            string logo = Path.Combine(root, "logo.bin");
            string arm9OverlayTable = Path.Combine(root, "y9.bin");
            string arm7OverlayTable = Path.Combine(root, "y7.bin");
            string expected = Path.Combine(root, "ndstool.nds");
            string actual = Path.Combine(root, "ndsforge.nds");
            byte[] arm9Bytes = [0xA9, 1, 2];
            byte[] arm7Bytes = [0xA7, 3];
            byte[] overlayBytes = [0x70, 0x71, 0x72, 0x73, 0x74];
            byte[] arm7OverlayBytes = [0x80, 0x81, 0x82];
            byte[] table = new byte[32];
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(0x00), 7);
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(0x04), 0x02010000);
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(0x08), 5);
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(0x0C), 3);
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(0x10), 0x02010000);
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(0x14), 0x02010004);
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(0x18), 0);
            byte[] arm7Table = new byte[32];
            BinaryPrimitives.WriteUInt32LittleEndian(arm7Table.AsSpan(0x00), 42);
            BinaryPrimitives.WriteUInt32LittleEndian(arm7Table.AsSpan(0x04), 0x02390000);
            BinaryPrimitives.WriteUInt32LittleEndian(arm7Table.AsSpan(0x08), 3);
            BinaryPrimitives.WriteUInt32LittleEndian(arm7Table.AsSpan(0x18), 1);
            await File.WriteAllBytesAsync(arm9, arm9Bytes, TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllBytesAsync(arm7, arm7Bytes, TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllBytesAsync(logo, new byte[156], TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllBytesAsync(arm9OverlayTable, table, TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllBytesAsync(arm7OverlayTable, arm7Table, TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllBytesAsync(
                Path.Combine(dataDirectory, "named.bin"),
                [0x40, 0x41],
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllBytesAsync(
                Path.Combine(overlayDirectory, "overlay_0000.bin"),
                overlayBytes,
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllBytesAsync(
                Path.Combine(overlayDirectory, "overlay_0001.bin"),
                arm7OverlayBytes,
                TestContext.Current.CancellationToken).ConfigureAwait(true);

            await RunAsync(
                ndstool,
                expected,
                arm9,
                arm7,
                logo,
                dataDirectory,
                banner: null,
                arm9OverlayTable,
                arm7OverlayTable,
                overlayDirectory).ConfigureAwait(true);
            var builder = new NdsImageBuilder
            {
                Title = "BUILD TEST",
                GameCode = "BT01",
                MakerCode = "HB",
                Arm9 = new(NdsProcessor.Arm9, arm9Bytes, 0x02000000, 0x02000000),
                Arm7 = new(NdsProcessor.Arm7, arm7Bytes, 0x02380000, 0x02380000),
            };
            builder.SetNintendoLogo(new byte[156]);
            builder.FileSystem.AddFile("/named.bin", [0x40, 0x41]);
            builder.AddOverlay(new NdsOverlayDefinition(
                NdsProcessor.Arm9,
                id: 7,
                overlayBytes,
                loadAddress: 0x02010000,
                ramSize: 5,
                bssSize: 3,
                staticInitializerStart: 0x02010000,
                staticInitializerEnd: 0x02010004));
            builder.AddOverlay(new NdsOverlayDefinition(
                NdsProcessor.Arm7,
                id: 42,
                arm7OverlayBytes,
                loadAddress: 0x02390000,
                ramSize: 3));
            await builder.WriteAsync(
                actual,
                new() { Profile = NdsImageBuildProfile.Ndstool1503 },
                TestContext.Current.CancellationToken).ConfigureAwait(true);

            byte[] expectedBytes = await File.ReadAllBytesAsync(expected, TestContext.Current.CancellationToken).ConfigureAwait(true);
            byte[] actualBytes = await File.ReadAllBytesAsync(actual, TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.True(expectedBytes.SequenceEqual(actualBytes), DescribeDifference(expectedBytes, actualBytes));
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

    private static string DescribeDifference(byte[] expected, byte[] actual)
    {
        int comparableLength = Math.Min(expected.Length, actual.Length);
        int[] offsets = Enumerable.Range(0, comparableLength)
            .Where(index => expected[index] != actual[index])
            .Take(16)
            .ToArray();
        static uint Read(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
        return $"Lengths: {expected.Length:X}/{actual.Length:X}; " +
            $"used: {Read(expected, 0x80):X}/{Read(actual, 0x80):X}; " +
            $"ARM9: {Read(expected, 0x20):X}+{Read(expected, 0x2C):X}/{Read(actual, 0x20):X}+{Read(actual, 0x2C):X}; " +
            $"ARM7: {Read(expected, 0x30):X}+{Read(expected, 0x3C):X}/{Read(actual, 0x30):X}+{Read(actual, 0x3C):X}; " +
            $"Y9: {Read(expected, 0x50):X}+{Read(expected, 0x54):X}/{Read(actual, 0x50):X}+{Read(actual, 0x54):X}; " +
            $"banner: {Read(expected, 0x68):X}/{Read(actual, 0x68):X}; " +
            $"FNT: {Read(expected, 0x40):X}/{Read(actual, 0x40):X}; " +
            $"FAT: {Read(expected, 0x48):X}/{Read(actual, 0x48):X}; " +
            $"at 47F0: {Convert.ToHexString(expected.AsSpan(0x47F0, 80))}/{Convert.ToHexString(actual.AsSpan(0x47F0, 80))}; " +
            $"first differences: {string.Join(", ", offsets.Select(offset => offset.ToString("X", CultureInfo.InvariantCulture)))}";
    }

    private static async Task RunAsync(
        string ndstool,
        string output,
        string arm9,
        string arm7,
        string logo,
        string data,
        string? banner,
        string? arm9OverlayTable = null,
        string? arm7OverlayTable = null,
        string? overlayDirectory = null)
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

        if (banner is not null)
        {
            info.ArgumentList.Add("-t");
            info.ArgumentList.Add(banner);
        }

        if (arm9OverlayTable is not null)
        {
            info.ArgumentList.Add("-y9");
            info.ArgumentList.Add(arm9OverlayTable);
        }

        if (arm7OverlayTable is not null)
        {
            info.ArgumentList.Add("-y7");
            info.ArgumentList.Add(arm7OverlayTable);
        }

        if (overlayDirectory is not null)
        {
            info.ArgumentList.Add("-y");
            info.ArgumentList.Add(overlayDirectory);
        }

        using var process = Process.Start(info)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.True(process.ExitCode == 0, $"{await stdout.ConfigureAwait(true)}\n{await stderr.ConfigureAwait(true)}");
    }
}
