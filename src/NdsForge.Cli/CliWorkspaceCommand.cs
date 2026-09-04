namespace NdsForge.Cli;

/// <summary>Exposes self-contained workspace extraction and strictly verified byte-exact packing.</summary>
internal static class CliWorkspaceCommand
{
    /// <summary>Creates a new workspace rather than merging inputs into an existing directory.</summary>
    internal static async Task<int> UnpackAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length != 3) { return Usage("Usage: ndsforge unpack <image.nds> <new-workspace-directory>"); }
        using NdsImage image = await NdsImage.OpenAsync(args[1], cancellationToken: cancellationToken).ConfigureAwait(false);
        NdsWorkspaceRecipe recipe = await NdsImageWorkspace.ExportAsync(image, args[2], cancellationToken).ConfigureAwait(false);
        Console.WriteLine(FormattableString.Invariant($"Exported workspace schema {recipe.SchemaVersion}: {recipe.Assets.Count} native assets plus a complete preservation snapshot."));
        Console.WriteLine($"Recipe: {Path.Combine(Path.GetFullPath(args[2]), NdsWorkspaceRecipe.FileName)}");
        PrintFindings(image);
        return 0;
    }

    /// <summary>Requires every input to match the exported baseline before publishing a complete identical image.</summary>
    internal static async Task<int> PackAsync(string[] args, CancellationToken cancellationToken)
    {
        bool overwrite = args.Length == 4 && args[3] == "--overwrite";
        if (args.Length is < 3 or > 4 || (args.Length == 4 && !overwrite))
        {
            return Usage("Usage: ndsforge pack <workspace-directory> <output.nds> [--overwrite]");
        }
        NdsWorkspaceRecipe recipe = await NdsImageWorkspace.PackFileAsync(args[1], args[2], overwrite, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(FormattableString.Invariant($"Packed {recipe.SourceInventory.PhysicalLength} bytes exactly; SHA-256 {recipe.SourceInventory.ImageSha256}."));
        using NdsImage image = await NdsImage.OpenAsync(args[2], cancellationToken: cancellationToken).ConfigureAwait(false);
        PrintFindings(image);
        return 0;
    }

    /// <summary>Reports preserved source findings without misrepresenting byte-exact packing as repair or authentication.</summary>
    private static void PrintFindings(NdsImage image)
    {
        foreach (NdsDiagnostic diagnostic in image.Validate().Diagnostics)
        {
            Console.Error.WriteLine($"Preserved source finding: {diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
        }
    }

    /// <summary>Separates unsupported command syntax from data or filesystem errors.</summary>
    private static int Usage(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }
}
