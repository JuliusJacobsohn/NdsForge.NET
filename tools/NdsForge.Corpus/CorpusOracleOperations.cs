namespace NdsForge.Corpus;

/// <summary>Runs only ndstool operations whose results are consumed by a NdsForge differential assertion.</summary>
internal static class CorpusOracleOperations
{
    /// <summary>Defines the asserted operation count used to invalidate obsolete or interrupted oracle records.</summary>
    public const int ExpectedOperationCount = 4;

    /// <summary>Captures extraction, binary reconstruction, CRC repair, and ARM7 hooking for one exact image.</summary>
    /// <param name="ndstool">Historical executable path identified separately by SHA-256.</param>
    /// <param name="romPath">Immutable source image copied before every mutating action.</param>
    /// <param name="workspace">Empty disposable directory dedicated to this image.</param>
    /// <returns>Only operations with corresponding byte or semantic differential tests.</returns>
    public static async Task<IReadOnlyList<OracleOperation>> RunAllAsync(
        string ndstool,
        string romPath,
        string workspace)
    {
        await using NdsImage image = await NdsImage.OpenAsync(romPath).ConfigureAwait(false);
        OracleOperation extraction = await ExtractAsync(ndstool, romPath, workspace, image.Header.Kind)
            .ConfigureAwait(false);
        return
        [
            extraction,
            await RebuildBinaryAsync(ndstool, workspace, image, extraction.ExitCode == 0).ConfigureAwait(false),
            await RepairHeaderAsync(ndstool, romPath, workspace).ConfigureAwait(false),
            await HookAsync(ndstool, romPath, workspace).ConfigureAwait(false),
        ];
    }

    /// <summary>Extracts every raw component, Overlay payload, and named NitroFS file for hash comparison.</summary>
    /// <param name="ndstool">Oracle executable.</param>
    /// <param name="romPath">Read-only source image.</param>
    /// <param name="workspace">Directory receiving component files.</param>
    /// <param name="kind">Controls whether DSi-mode executable switches are included.</param>
    /// <returns>Process result plus hashes for every produced artifact.</returns>
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

    /// <summary>Rebuilds extracted content so the test suite can compare structural semantics and legacy image extent.</summary>
    /// <param name="ndstool">Oracle executable.</param>
    /// <param name="workspace">Directory containing extracted inputs.</param>
    /// <param name="image">Parsed source supplying typed identity and runtime addresses.</param>
    /// <param name="available">Whether extraction completed successfully.</param>
    /// <returns>Build status and rebuilt-image digest, or an explicit prerequisite failure.</returns>
    private static Task<OracleOperation> RebuildBinaryAsync(
        string ndstool,
        string workspace,
        NdsImage image,
        bool available)
    {
        if (!available)
        {
            return Task.FromResult(Skipped("create-binary", "Extraction failed."));
        }

        List<string> arguments = CreateCommonBuildArguments("rebuilt-binary.nds", image);
        arguments.AddRange(["-9", "arm9.bin", "-7", "arm7.bin", "-d", "data", "-y", "overlays",
            "-y9", "arm9-overlays.bin", "-y7", "arm7-overlays.bin", "-t", "banner.bin", "-o", "logo.bin"]);
        if (image.Header.Kind != NdsImageKind.NintendoDs)
        {
            arguments.AddRange(["-9i", "arm9i.bin", "-7i", "arm7i.bin"]);
        }

        return RunWithArtifactsAsync(ndstool, workspace, "create-binary", arguments, ["rebuilt-binary.nds"]);
    }

    /// <summary>Corrupts only the stored common-header CRC and records ndstool's resulting full image.</summary>
    /// <param name="ndstool">Oracle executable.</param>
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

        return await RunWithArtifactsAsync(
            ndstool,
            workspace,
            "repair-header-crc",
            ["-f", "repaired-header.nds"],
            ["repaired-header.nds"]).ConfigureAwait(false);
    }

    /// <summary>Applies an aligned ARM no-op trainer for whole-image byte comparison.</summary>
    /// <param name="ndstool">Oracle executable.</param>
    /// <param name="romPath">Immutable source copied before hooking.</param>
    /// <param name="workspace">Disposable output directory.</param>
    /// <returns>Hooked image and hook-file digests.</returns>
    private static async Task<OracleOperation> HookAsync(string ndstool, string romPath, string workspace)
    {
        File.Copy(romPath, Path.Combine(workspace, "hooked.nds"), overwrite: true);
        await File.WriteAllBytesAsync(Path.Combine(workspace, "hook.bin"), [0x00, 0x00, 0xA0, 0xE1])
            .ConfigureAwait(false);
        return await RunWithArtifactsAsync(
            ndstool,
            workspace,
            "hook-arm7",
            ["-k", "hooked.nds", "-7", "hook.bin"],
            ["hooked.nds", "hook.bin"]).ConfigureAwait(false);
    }

    /// <summary>Builds the versioned ndstool creation arguments consumed by the structural differential.</summary>
    /// <param name="output">Workspace-relative output image.</param>
    /// <param name="image">Parsed source supplying identity and addresses.</param>
    /// <returns>Mutable arguments ready for extracted component inputs.</returns>
    private static List<string> CreateCommonBuildArguments(string output, NdsImage image) =>
    [
        "-c", output,
        "-h", "header.bin",
        "-g", image.Header.GameCode, image.Header.MakerCode, image.Header.Title,
        image.Header.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "-r9", $"0x{image.Header.Arm9.LoadAddress:X8}", "-e9", $"0x{image.Header.Arm9.EntryAddress:X8}",
        "-r7", $"0x{image.Header.Arm7.LoadAddress:X8}", "-e7", $"0x{image.Header.Arm7.EntryAddress:X8}",
        "-n", "0", "0",
    ];

    /// <summary>Runs an output-producing operation and attaches hashes for its asserted artifacts.</summary>
    /// <param name="ndstool">Oracle executable.</param>
    /// <param name="workspace">Artifact root and working directory.</param>
    /// <param name="name">Stable operation identity.</param>
    /// <param name="arguments">Exact invocation arguments.</param>
    /// <param name="artifacts">Files or trees consumed by differential tests.</param>
    /// <returns>Process record augmented with artifact identities.</returns>
    private static async Task<OracleOperation> RunWithArtifactsAsync(
        string ndstool,
        string workspace,
        string name,
        IReadOnlyList<string> arguments,
        IReadOnlyList<string> artifacts)
    {
        OracleOperation operation = await NdstoolProcess.RunAsync(ndstool, workspace, name, arguments)
            .ConfigureAwait(false);
        return operation with { Artifacts = await OracleArtifacts.CaptureAsync(workspace, artifacts).ConfigureAwait(false) };
    }

    /// <summary>Records a failed prerequisite without pretending ndstool performed the dependent operation.</summary>
    /// <param name="name">Operation that could not run.</param>
    /// <param name="reason">Human-readable prerequisite failure.</param>
    /// <returns>Nonzero record with no artifacts.</returns>
    private static OracleOperation Skipped(string name, string reason) => new(name, [], -1, 0, string.Empty, reason, []);

    /// <summary>Replaces the private source path with a portable marker before writing ignored oracle metadata.</summary>
    /// <param name="arguments">Actual invocation arguments.</param>
    /// <param name="romPath">Private absolute source path.</param>
    /// <returns>Detached redacted argument array.</returns>
    private static string[] Redact(IEnumerable<string> arguments, string romPath) => arguments
        .Select(argument => argument.Equals(romPath, StringComparison.OrdinalIgnoreCase) ? "{ROM}" : argument)
        .ToArray();
}
