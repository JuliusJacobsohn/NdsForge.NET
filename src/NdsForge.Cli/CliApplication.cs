using System.Globalization;

namespace NdsForge.Cli;

internal static class CliApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += handler;
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
            {
                PrintHelp();
                return 0;
            }

            return args[0].ToUpperInvariant() switch
            {
                "INSPECT" or "INFO" => await InspectAsync(args, cancellation.Token).ConfigureAwait(false),
                "VALIDATE" => await ValidateAsync(args, cancellation.Token).ConfigureAwait(false),
                "LIST" or "LS" => await ListAsync(args, cancellation.Token).ConfigureAwait(false),
                "EXTRACT" => await ExtractAsync(args, cancellation.Token).ConfigureAwait(false),
                "UNPACK" => await CliWorkspaceCommand.UnpackAsync(args, cancellation.Token).ConfigureAwait(false),
                "PACK" => await CliWorkspaceCommand.PackAsync(args, cancellation.Token).ConfigureAwait(false),
                "REPLACE" => await ReplaceAsync(args, cancellation.Token).ConfigureAwait(false),
                "RESIZE" => await CliResizeCommand.RunAsync(args, cancellation.Token).ConfigureAwait(false),
                "MANIFEST" => await ManifestAsync(args, cancellation.Token).ConfigureAwait(false),
                "DIFF" => await DiffAsync(args, cancellation.Token).ConfigureAwait(false),
                _ => InvalidArguments($"Unknown command '{args[0]}'."),
            };
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    private static async Task<int> InspectAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length != 2)
        {
            return InvalidArguments("Usage: ndsforge inspect <image.nds>");
        }

        using NdsImage image = await NdsImage.OpenAsync(args[1], cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        NdsHeader header = image.Header;
        Console.WriteLine($"Title:            {header.Title}");
        Console.WriteLine($"Game code:        {header.GameCode}");
        Console.WriteLine($"Maker code:       {header.MakerCode}");
        Console.WriteLine($"Image kind:       {header.Kind}");
        Console.WriteLine($"Storage carrier:  {image.CarrierLayout.Kind}");
        Console.WriteLine($"Version:          {header.Version}");
        Console.WriteLine($"Physical size:    {FormatSize(image.Length)}");
        Console.WriteLine($"Declared used:    {FormatSize(header.UsedImageSize)}");
        NdsImageSizeInfo sizes = image.SizeInfo;
        Console.WriteLine($"Declared extent:  {FormatSize(sizes.DeclaredContentEnd)}");
        Console.WriteLine($"Device capacity:  {(sizes.DeviceCapacityBytes is long capacity ? FormatSize(capacity) : "unrepresentable")}, exponent {sizes.DeviceCapacityExponent}");
        if (sizes.DsiUsedSize is uint dsiSize) { Console.WriteLine($"DSi total used:   {FormatSize(dsiSize)}"); }
        Console.WriteLine($"Trailing bytes:   {FormatSize(sizes.TrailingData?.Length ?? 0)} (not assumed padding)");
        PrintProgram(header.Arm9);
        PrintProgram(header.Arm7);
        if (header.Arm9i is not null)
        {
            PrintProgram(header.Arm9i);
        }

        if (header.Arm7i is not null)
        {
            PrintProgram(header.Arm7i);
        }

        Console.WriteLine($"NitroFS:          {image.FileSystem.Files.Count:N0} named files, {image.FileSystem.Directories.Count:N0} directories");
        Console.WriteLine($"Overlays:         {image.Arm9Overlays.Count:N0} ARM9, {image.Arm7Overlays.Count:N0} ARM7");
        Console.WriteLine(image.Banner is null
            ? "Banner:           absent"
            : $"Banner:           version 0x{image.Banner.Version:X4}, {image.Banner.LanguageCount} languages");
        NdsValidationResult validation = image.Validate();
        Console.WriteLine($"Validation:       {(validation.IsValid ? "valid" : $"{validation.Diagnostics.Count} finding(s)")}");
        return validation.IsValid ? 0 : 1;
    }

    private static async Task<int> ValidateAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length != 2)
        {
            return InvalidArguments("Usage: ndsforge validate <image.nds>");
        }

        using NdsImage image = await NdsImage.OpenAsync(args[1], cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        NdsValidationResult result = image.Validate();
        if (result.Diagnostics.Count == 0)
        {
            Console.WriteLine("Image is valid.");
            return 0;
        }

        foreach (NdsDiagnostic diagnostic in result.Diagnostics)
        {
            string region = diagnostic.Region is null
                ? string.Empty
                : $" at 0x{diagnostic.Region.Value.Offset:X}+0x{diagnostic.Region.Value.Length:X}";
            Console.WriteLine($"{diagnostic.Severity} {diagnostic.Code}{region}: {diagnostic.Message}");
        }

        return result.IsValid ? 0 : 1;
    }

    private static async Task<int> ListAsync(string[] args, CancellationToken cancellationToken)
    {
        bool detailed = args.Length == 3 && args[2] is "--long" or "-l";
        if (args.Length is < 2 or > 3 || (args.Length == 3 && !detailed))
        {
            return InvalidArguments("Usage: ndsforge list <image.nds> [--long]");
        }

        using NdsImage image = await NdsImage.OpenAsync(args[1], cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        foreach (NdsFile file in image.FileSystem.Files)
        {
            if (detailed)
            {
                Console.WriteLine(FormattableString.Invariant(
                    $"{file.Id,6}  0x{file.Data.Offset:X8}  0x{file.Data.Length:X8}  {file.FullPath}"));
            }
            else
            {
                Console.WriteLine(file.FullPath);
            }
        }

        return 0;
    }

    private static async Task<int> ExtractAsync(string[] args, CancellationToken cancellationToken)
    {
        bool overwrite = args.Length == 4 && args[3] == "--overwrite";
        if (args.Length is < 3 or > 4 || (args.Length == 4 && !overwrite))
        {
            return InvalidArguments("Usage: ndsforge extract <image.nds> <directory> [--overwrite]");
        }

        using NdsImage image = await NdsImage.OpenAsync(args[1], cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        NdsExtractionResult result = await image.ExtractAsync(
            args[2],
            new()
            {
                OverwritePolicy = overwrite ? NdsOverwritePolicy.Overwrite : NdsOverwritePolicy.Fail,
            },
            cancellationToken).ConfigureAwait(false);
        Console.WriteLine(
            $"Extracted {result.WrittenFiles:N0} files ({FormatSize(result.WrittenBytes)}); skipped {result.SkippedFiles:N0}.");
        return 0;
    }

    private static async Task<int> ReplaceAsync(string[] args, CancellationToken cancellationToken)
    {
        bool overwrite = args.Length == 6 && args[5] == "--overwrite";
        if (args.Length is < 5 or > 6 || (args.Length == 6 && !overwrite))
        {
            return InvalidArguments(
                "Usage: ndsforge replace <image.nds> <nitro-path> <file> <output.nds> [--overwrite]");
        }

        byte[] contents = await File.ReadAllBytesAsync(args[3], cancellationToken).ConfigureAwait(false);
        using NdsImage image = await NdsImage.OpenAsync(args[1], cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        NdsImageEditor editor = image.Edit().ReplaceFile(args[2], contents);
        NdsFileChange change = AssertSingleChange(editor);
        NdsSaveResult result = await editor.SaveAsync(
            args[4],
            new() { OverwriteDestination = overwrite },
            cancellationToken).ConfigureAwait(false);
        Console.WriteLine(
            $"Replaced {change.Path} ({change.OriginalLength:N0} -> {change.ReplacementLength:N0} bytes); " +
            $"{result.RelocatedFiles:N0} relocation(s), used size {FormatSize(result.UsedImageSize)}.");
        foreach (NdsDiagnostic diagnostic in result.Diagnostics)
        {
            Console.Error.WriteLine($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
        }
        return 0;
    }

    private static async Task<int> ManifestAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length is < 2 or > 3)
        {
            return InvalidArguments("Usage: ndsforge manifest <image.nds> [output.json]");
        }

        using NdsImage image = await NdsImage.OpenAsync(args[1], cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        NdsImageManifest manifest = await image.CreateManifestAsync(cancellationToken).ConfigureAwait(false);
        if (args.Length == 2)
        {
            Console.WriteLine(manifest.ToJson());
            return 0;
        }

        var stream = new FileStream(
            args[2],
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous);
        await using (stream.ConfigureAwait(false))
        {
            await manifest.WriteJsonAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        Console.WriteLine($"Wrote manifest for {manifest.Header.GameCode} to {Path.GetFullPath(args[2])}.");
        return 0;
    }

    private static async Task<int> DiffAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length != 3)
        {
            return InvalidArguments("Usage: ndsforge diff <left.nds> <right.nds>");
        }

        using NdsImage left = await NdsImage.OpenAsync(args[1], cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        using NdsImage right = await NdsImage.OpenAsync(args[2], cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        NdsImageDiff diff = await left.CompareAsync(right, cancellationToken).ConfigureAwait(false);
        foreach (NdsSemanticDifference difference in diff.Differences)
        {
            Console.WriteLine($"{difference.Kind,-10} {difference.Path}: {difference.Before ?? "<absent>"} -> {difference.After ?? "<absent>"}");
        }

        if (diff.AreEquivalent)
        {
            Console.WriteLine("Images are semantically and physically equivalent.");
            return 0;
        }

        Console.WriteLine($"{diff.Differences.Count:N0} difference(s).");
        return 1;
    }

    private static NdsFileChange AssertSingleChange(NdsImageEditor editor)
    {
        if (editor.Changes.Count != 1)
        {
            throw new InvalidOperationException("The replace command expected exactly one change.");
        }

        return editor.Changes[0];
    }

    private static void PrintProgram(NdsProgram program)
    {
        string footer = program.Footer is null ? string.Empty : " + 12-byte footer";
        Console.WriteLine(FormattableString.Invariant(
            $"{program.Processor,-17} ROM 0x{program.Data.Offset:X8}+0x{program.Data.Length:X}, load 0x{program.LoadAddress:X8}, entry 0x{program.EntryAddress:X8}{footer}"));
    }

    private static string FormatSize(long bytes) => string.Create(
        CultureInfo.CurrentCulture,
        $"{bytes:N0} bytes (0x{bytes:X})");

    private static int InvalidArguments(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("Run 'ndsforge help' for commands.");
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("NdsForge.NET — Nintendo DS/DSi image toolkit");
        Console.WriteLine();
        Console.WriteLine("Usage: ndsforge <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  inspect <image.nds>                  Show structured image information");
        Console.WriteLine("  validate <image.nds>                 Validate checksums, ranges, and references");
        Console.WriteLine("  list <image.nds> [--long]            List named NitroFS files");
        Console.WriteLine("  extract <image.nds> <dir> [--overwrite]");
        Console.WriteLine("                                       Safely export all image components");
        Console.WriteLine("  replace <image.nds> <path> <file> <output.nds> [--overwrite]");
        Console.WriteLine("                                       Replace one NitroFS file and verify output");
        Console.WriteLine("  manifest <image.nds> [output.json]   Emit a strict SHA-256 image manifest");
        Console.WriteLine("  resize <input.nds> <output.nds> <preserve|trim|pad|exact> [options]");
        Console.WriteLine("                                       Resize without moving content or changing headers");
        Console.WriteLine("  unpack <image.nds> <new-directory>    Export a self-contained image workspace");
        Console.WriteLine("  pack <workspace> <output.nds> [--overwrite]");
        Console.WriteLine("                                       Verify unchanged inputs and pack byte-exactly");
        Console.WriteLine("  diff <left.nds> <right.nds>          Compare content, identities, and layout");
    }
}
