namespace NdsForge.Corpus;

/// <summary>Exercises every ndstool action plus binary, ELF, bitmap, wildcard, DSi-option, and address-setting branches.</summary>
internal static class CorpusOracleOperations
{
    /// <summary>Defines the complete record count used to distinguish a finished oracle from an interrupted one.</summary>
    public const int ExpectedOperationCount = 16;

    /// <summary>Runs read-only, extraction, creation, repair, encryption, and trainer transforms on one image.</summary>
    /// <param name="ndstool">Historical executable path.</param>
    /// <param name="romPath">Private source image, never passed to a mutating action.</param>
    /// <param name="workspace">Empty disposable directory dedicated to this image.</param>
    /// <returns>All operations in a stable feature-oriented order.</returns>
    public static async Task<IReadOnlyList<OracleOperation>> RunAllAsync(
        string ndstool,
        string romPath,
        string workspace)
    {
        var operations = new List<OracleOperation>(ExpectedOperationCount);
        await using NdsImage image = await NdsImage.OpenAsync(romPath).ConfigureAwait(false);
        operations.Add(await RunReadOnlyAsync(ndstool, workspace, romPath, "info", ["-i", romPath]).ConfigureAwait(false));
        operations.Add(await RunReadOnlyAsync(ndstool, workspace, romPath, "verbose-info", ["-vv", "-i", romPath]).ConfigureAwait(false));
        operations.Add(await RunReadOnlyAsync(ndstool, workspace, romPath, "list", ["-l", romPath]).ConfigureAwait(false));
        operations.Add(await RunReadOnlyAsync(
            ndstool,
            workspace,
            romPath,
            "wildcard-list",
            ["-v", "-l", romPath, "-w", "*.bin", "*.sdat", "*.narc"]).ConfigureAwait(false));

        OracleOperation extraction = await ExtractAsync(ndstool, romPath, workspace, image.Header.Kind).ConfigureAwait(false);
        operations.Add(extraction);
        operations.Add(await RebuildBinaryAsync(ndstool, workspace, image, extraction.ExitCode == 0).ConfigureAwait(false));
        operations.Add(await RebuildElfAsync(ndstool, workspace, image, extraction.ExitCode == 0).ConfigureAwait(false));
        operations.Add(await RebuildBitmapAsync(ndstool, workspace, image, extraction.ExitCode == 0).ConfigureAwait(false));
        operations.Add(await ProbeDsiOptionsAsync(ndstool, workspace).ConfigureAwait(false));
        operations.Add(await RepairHeaderAsync(ndstool, romPath, workspace).ConfigureAwait(false));
        operations.Add(await HookAsync(ndstool, romPath, workspace).ConfigureAwait(false));

        OracleOperation decrypt = await TransformSecureAreaAsync(
            ndstool,
            romPath,
            workspace,
            "secure-decrypt",
            "-sd",
            "secure-decrypted.nds").ConfigureAwait(false);
        operations.Add(decrypt);
        string decrypted = Path.Combine(workspace, "secure-decrypted.nds");
        operations.Add(await TransformSecureAreaAsync(
            ndstool,
            decrypt.ExitCode == 0 ? decrypted : romPath,
            workspace,
            "secure-encrypt-nintendo",
            "-se",
            "secure-encrypted-nintendo.nds").ConfigureAwait(false));
        operations.Add(await TransformSecureAreaAsync(
            ndstool,
            decrypt.ExitCode == 0 ? decrypted : romPath,
            workspace,
            "secure-encrypt-other",
            "-sE",
            "secure-encrypted-other.nds").ConfigureAwait(false));
        operations.Add(await RunReadOnlyAsync(ndstool, workspace, romPath, "verbose-list", ["-vvv", "-l", romPath]).ConfigureAwait(false));
        operations.Add(await RunReadOnlyAsync(ndstool, workspace, romPath, "post-oracle-info", ["-v", "-i", romPath]).ConfigureAwait(false));
        return operations;
    }

    /// <summary>Runs a nonmutating action and replaces the machine-specific ROM path in recorded arguments.</summary>
    /// <param name="ndstool">Executable path.</param>
    /// <param name="workspace">Current disposable working directory.</param>
    /// <param name="romPath">Absolute source path redacted from the record.</param>
    /// <param name="name">Stable operation identity.</param>
    /// <param name="arguments">Actual invocation arguments.</param>
    /// <returns>Captured console and process status.</returns>
    private static Task<OracleOperation> RunReadOnlyAsync(
        string ndstool,
        string workspace,
        string romPath,
        string name,
        IReadOnlyList<string> arguments) =>
        NdstoolProcess.RunAsync(ndstool, workspace, name, arguments, Redact(arguments, romPath));

    /// <summary>Extracts every raw component, overlay payload, and named NitroFS file in one tool invocation.</summary>
    /// <param name="ndstool">Executable path.</param>
    /// <param name="romPath">Read-only source image.</param>
    /// <param name="workspace">Directory receiving component files.</param>
    /// <param name="kind">Controls whether DSi-mode executable switches are included.</param>
    /// <returns>Operation plus hashes for every extracted file.</returns>
    private static async Task<OracleOperation> ExtractAsync(
        string ndstool,
        string romPath,
        string workspace,
        NdsImageKind kind)
    {
        var arguments = new List<string>
        {
            "-x", romPath,
            "-9", "arm9.bin",
            "-7", "arm7.bin",
            "-y9", "arm9-overlays.bin",
            "-y7", "arm7-overlays.bin",
            "-d", "data",
            "-y", "overlays",
            "-t", "banner.bin",
            "-h", "header.bin",
            "-o", "logo.bin",
        };
        if (kind != NdsImageKind.NintendoDs)
        {
            arguments.AddRange(["-9i", "arm9i.bin", "-7i", "arm7i.bin"]);
        }

        OracleOperation operation = await NdstoolProcess.RunAsync(
            ndstool,
            workspace,
            "extract-all",
            arguments,
            Redact(arguments, romPath)).ConfigureAwait(false);
        IReadOnlyList<OracleArtifact> artifacts = await OracleArtifacts.CaptureAsync(
            workspace,
            ["arm9.bin", "arm7.bin", "arm9-overlays.bin", "arm7-overlays.bin", "data", "overlays",
                "banner.bin", "header.bin", "logo.bin", "arm9i.bin", "arm7i.bin"]).ConfigureAwait(false);
        return operation with { Artifacts = artifacts };
    }

    /// <summary>Rebuilds extracted binaries with original identity, addresses, tables, banner, header, logo, and latency switches.</summary>
    /// <param name="ndstool">Executable path.</param>
    /// <param name="workspace">Directory containing extracted inputs.</param>
    /// <param name="image">Parsed source supplying typed address and identity values.</param>
    /// <param name="available">Whether extraction produced trustworthy inputs.</param>
    /// <returns>Build status and rebuilt image digest, or a synthetic skipped operation.</returns>
    private static async Task<OracleOperation> RebuildBinaryAsync(
        string ndstool,
        string workspace,
        NdsImage image,
        bool available)
    {
        if (!available)
        {
            return Skipped("create-binary", "Extraction failed.");
        }

        var arguments = CreateCommonBuildArguments("rebuilt-binary.nds", image, useTemplate: true);
        arguments.AddRange(["-9", "arm9.bin", "-7", "arm7.bin", "-d", "data", "-y", "overlays",
            "-y9", "arm9-overlays.bin", "-y7", "arm7-overlays.bin", "-t", "banner.bin", "-o", "logo.bin"]);
        if (image.Header.Kind != NdsImageKind.NintendoDs)
        {
            arguments.AddRange(["-9i", "arm9i.bin", "-7i", "arm7i.bin"]);
        }

        return await RunWithArtifactsAsync(ndstool, workspace, "create-binary", arguments, ["rebuilt-binary.nds"])
            .ConfigureAwait(false);
    }

    /// <summary>Exercises ndstool's ARM ELF reader using deterministic loadable prefixes derived from both source programs.</summary>
    /// <param name="ndstool">Executable path.</param>
    /// <param name="workspace">Directory containing extracted binaries.</param>
    /// <param name="image">Parsed source supplying ELF runtime addresses.</param>
    /// <param name="available">Whether program extraction succeeded.</param>
    /// <returns>ELF-based image build result.</returns>
    private static async Task<OracleOperation> RebuildElfAsync(
        string ndstool,
        string workspace,
        NdsImage image,
        bool available)
    {
        if (!available)
        {
            return Skipped("create-elf", "Extraction failed.");
        }

        await OracleNativeAssets.WriteElfAsync(
            Path.Combine(workspace, "arm9.bin"),
            Path.Combine(workspace, "arm9.elf"),
            image.Header.Arm9.LoadAddress,
            image.Header.Arm9.EntryAddress).ConfigureAwait(false);
        await OracleNativeAssets.WriteElfAsync(
            Path.Combine(workspace, "arm7.bin"),
            Path.Combine(workspace, "arm7.elf"),
            image.Header.Arm7.LoadAddress,
            image.Header.Arm7.EntryAddress).ConfigureAwait(false);
        Directory.CreateDirectory(Path.Combine(workspace, "empty-data"));
        var arguments = CreateCommonBuildArguments("rebuilt-elf.nds", image, useTemplate: false);
        arguments.AddRange(["-9", "arm9.elf", "-7", "arm7.elf", "-d", "empty-data"]);
        return await RunWithArtifactsAsync(ndstool, workspace, "create-elf", arguments, ["rebuilt-elf.nds"])
            .ConfigureAwait(false);
    }

    /// <summary>Exercises indexed banner and logo conversion together with every DSi-specific creation switch.</summary>
    /// <param name="ndstool">Executable path.</param>
    /// <param name="workspace">Directory containing generated ELF inputs.</param>
    /// <param name="image">Parsed source supplying safe identity defaults.</param>
    /// <param name="available">Whether prerequisite extraction succeeded.</param>
    /// <returns>Bitmap-based image build result.</returns>
    private static async Task<OracleOperation> RebuildBitmapAsync(
        string ndstool,
        string workspace,
        NdsImage image,
        bool available)
    {
        if (!available)
        {
            return Skipped("create-bitmaps", "Extraction failed.");
        }

        await OracleNativeAssets.WriteIndexedBmpAsync(Path.Combine(workspace, "banner.bmp"), 32, 32, 16).ConfigureAwait(false);
        await OracleNativeAssets.WriteIndexedBmpAsync(Path.Combine(workspace, "logo.bmp"), 104, 16, 2).ConfigureAwait(false);
        var arguments = CreateCommonBuildArguments("rebuilt-bitmaps.nds", image, useTemplate: false);
        arguments.AddRange(["-9", "arm9.elf", "-7", "arm7.elf", "-d", "empty-data",
            "-b", "banner.bmp", image.Header.Title, "-o", "logo.bmp", "-m", image.Header.MakerCode]);
        return await RunWithArtifactsAsync(
            ndstool,
            workspace,
            "create-bitmaps",
            arguments,
            ["rebuilt-bitmaps.nds"]).ConfigureAwait(false);
    }

    /// <summary>Records whether the installed historical binary recognizes its source tree's documented DSi switches.</summary>
    /// <param name="ndstool">Executable path whose compiled option surface is under test.</param>
    /// <param name="workspace">Disposable working directory.</param>
    /// <returns>Argument-parser evidence; the known 1.50.3 Windows binary rejects <c>-u</c> before creation.</returns>
    private static Task<OracleOperation> ProbeDsiOptionsAsync(string ndstool, string workspace) =>
        NdstoolProcess.RunAsync(
            ndstool,
            workspace,
            "dsi-options-probe",
            ["-c", "dsi-options-probe.nds", "-u", "00000000", "-z", "00000000", "-a", "00000000",
                "-p", "00", "-q", "00000000"]);

    /// <summary>Corrupts only the stored common-header CRC and records whether ndstool restores the original bytes.</summary>
    /// <param name="ndstool">Executable path.</param>
    /// <param name="romPath">Immutable original copied before corruption.</param>
    /// <param name="workspace">Disposable output directory.</param>
    /// <returns>Repair result and complete repaired-image digest.</returns>
    private static async Task<OracleOperation> RepairHeaderAsync(string ndstool, string romPath, string workspace)
    {
        string output = Path.Combine(workspace, "repaired-header.nds");
        File.Copy(romPath, output, overwrite: true);
        await using (var stream = new FileStream(output, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1, true))
        {
            stream.Position = 0x15E;
            var storedCrcByte = new byte[1];
            await stream.ReadExactlyAsync(storedCrcByte).ConfigureAwait(false);

            stream.Position = 0x15E;
            storedCrcByte[0] ^= 0xFF;
            await stream.WriteAsync(storedCrcByte).ConfigureAwait(false);
        }

        return await RunWithArtifactsAsync(ndstool, workspace, "repair-header-crc", ["-f", "repaired-header.nds"],
            ["repaired-header.nds"]).ConfigureAwait(false);
    }

    /// <summary>Applies an aligned ARM no-op trainer so deterministic hook bytes can be compared by the test suite.</summary>
    /// <param name="ndstool">Executable path.</param>
    /// <param name="romPath">Immutable source copied before hooking.</param>
    /// <param name="workspace">Disposable output directory.</param>
    /// <returns>Hooked image digest and console result.</returns>
    private static async Task<OracleOperation> HookAsync(string ndstool, string romPath, string workspace)
    {
        File.Copy(romPath, Path.Combine(workspace, "hooked.nds"), overwrite: true);
        await File.WriteAllBytesAsync(Path.Combine(workspace, "hook.bin"), [0x00, 0x00, 0xA0, 0xE1]).ConfigureAwait(false);
        return await RunWithArtifactsAsync(ndstool, workspace, "hook-arm7", ["-k", "hooked.nds", "-7", "hook.bin"],
            ["hooked.nds", "hook.bin"]).ConfigureAwait(false);
    }

    /// <summary>Runs one secure-area mode on a disposable copy so the private source remains immutable.</summary>
    /// <param name="ndstool">Executable path.</param>
    /// <param name="inputPath">Original or successfully decrypted source.</param>
    /// <param name="workspace">Disposable output directory.</param>
    /// <param name="name">Stable transform identity.</param>
    /// <param name="switchName">One of ndstool's <c>-sd</c>, <c>-se</c>, or <c>-sE</c> modes.</param>
    /// <param name="outputName">Workspace-relative mutable image copy.</param>
    /// <returns>Transform result and output digest even when the tool rejects the image state.</returns>
    private static async Task<OracleOperation> TransformSecureAreaAsync(
        string ndstool,
        string inputPath,
        string workspace,
        string name,
        string switchName,
        string outputName)
    {
        string output = Path.Combine(workspace, outputName);
        if (!Path.GetFullPath(inputPath).Equals(output, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(inputPath, output, overwrite: true);
        }

        return await RunWithArtifactsAsync(ndstool, workspace, name, [switchName, outputName], [outputName])
            .ConfigureAwait(false);
    }

    /// <summary>Builds shared create arguments covering header identity, runtime addresses, latency, and both header-size branches.</summary>
    /// <param name="output">Workspace-relative output image.</param>
    /// <param name="image">Parsed source supplying identity and addresses.</param>
    /// <param name="useTemplate">Whether the extracted header or numeric 16 KiB header size is selected.</param>
    /// <returns>Mutable argument list ready for content-specific switches.</returns>
    private static List<string> CreateCommonBuildArguments(string output, NdsImage image, bool useTemplate) =>
    [
        "-c", output,
        "-h", useTemplate ? "header.bin" : "0x4000",
        "-g", image.Header.GameCode, image.Header.MakerCode, image.Header.Title, image.Header.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "-r9", $"0x{image.Header.Arm9.LoadAddress:X8}", "-e9", $"0x{image.Header.Arm9.EntryAddress:X8}",
        "-r7", $"0x{image.Header.Arm7.LoadAddress:X8}", "-e7", $"0x{image.Header.Arm7.EntryAddress:X8}",
        "-n", "0", "0",
    ];

    /// <summary>Runs an output-producing operation and attaches hashes for its selected artifacts.</summary>
    /// <param name="ndstool">Executable path.</param>
    /// <param name="workspace">Artifact root and working directory.</param>
    /// <param name="name">Stable operation identity.</param>
    /// <param name="arguments">Exact invocation arguments.</param>
    /// <param name="artifacts">Files or trees owned by this operation.</param>
    /// <returns>Process record augmented with generated artifact identities.</returns>
    private static async Task<OracleOperation> RunWithArtifactsAsync(
        string ndstool,
        string workspace,
        string name,
        IReadOnlyList<string> arguments,
        IReadOnlyList<string> artifacts)
    {
        OracleOperation operation = await NdstoolProcess.RunAsync(ndstool, workspace, name, arguments).ConfigureAwait(false);
        return operation with { Artifacts = await OracleArtifacts.CaptureAsync(workspace, artifacts).ConfigureAwait(false) };
    }

    /// <summary>Creates a visible skipped record without pretending a prerequisite failure was a tool execution.</summary>
    /// <param name="name">Operation that could not run.</param>
    /// <param name="reason">Prerequisite failure retained in standard error.</param>
    /// <returns>Nonzero synthetic operation with no arguments or artifacts.</returns>
    private static OracleOperation Skipped(string name, string reason) => new(name, [], -1, 0, string.Empty, reason, []);

    /// <summary>Replaces a private absolute source path with a portable marker while retaining every other argument exactly.</summary>
    /// <param name="arguments">Actual invocation arguments.</param>
    /// <param name="romPath">Private source path to redact.</param>
    /// <returns>Detached recorded argument array.</returns>
    private static string[] Redact(IEnumerable<string> arguments, string romPath) => arguments
        .Select(argument => argument.Equals(romPath, StringComparison.OrdinalIgnoreCase) ? "{ROM}" : argument)
        .ToArray();
}
