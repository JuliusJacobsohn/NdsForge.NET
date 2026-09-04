namespace NdsForge.Cli;

/// <summary>Reconstructs supported workspace edits with deterministic layout and explicit authentication choices.</summary>
internal static class CliBuildCommand
{
    internal static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        CliBuildArguments? arguments = CliBuildArguments.Parse(args);
        if (arguments is null)
        {
            Console.Error.WriteLine("Usage: ndsforge build <workspace-directory> <output.nds> [--capacity <bytes>] [--pad] [--padding-byte <HH>] [--ds-integrity <preserve|clear> | --dsi-integrity <clear|homebrew>] [--overwrite]");
            Console.Error.WriteLine("Capacity must be a power of two from 128 KiB through 4 GiB (decimal or 0x-prefixed hex). DSi builds require an explicit integrity choice.");
            return 2;
        }
        string root = Path.GetFullPath(args[1]);
        string output = Path.GetFullPath(args[2]);
        CliBuildOutput.Check(root, output, arguments.BuildOptions.OverwriteDestination);
        NdsImageBuilder builder = await NdsImageWorkspace.ImportAsync(root, cancellationToken: cancellationToken).ConfigureAwait(false);
        ApplyIntegrity(builder, arguments);
        NdsImageBuildResult result = await CliBuildOutput.WriteAsync(builder, root, output, arguments.BuildOptions, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(FormattableString.Invariant($"Built {result.PhysicalSize} bytes with {result.AllocationCount} allocations; common used size {result.UsedSize}."));
        Console.WriteLine("Structural output is not byte-exact preservation and does not establish signature authenticity or hardware acceptance.");
        foreach (NdsDiagnostic diagnostic in result.Diagnostics)
        {
            Console.Error.WriteLine($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
        }
        return 0;
    }

    private static void ApplyIntegrity(NdsImageBuilder builder, CliBuildArguments arguments)
    {
        if (builder.DsiMetadata is { } dsi)
        {
            if (arguments.DsIntegrity is not null || arguments.DsiIntegrity is null)
            {
                throw new InvalidDataException("DSi structural builds require --dsi-integrity clear or homebrew; stored authentication is not retained.");
            }
            dsi.Integrity = arguments.DsiIntegrity == "clear" ? NdsDsiIntegrityOptions.Unauthenticated : NdsDsiIntegrityOptions.NdstoolHomebrew;
            Console.Error.WriteLine("DSi policy: component authentication is explicitly cleared or regenerated; source hierarchical digest tables are omitted. Use the library API for keyed digests or signing.");
        }
        else if (arguments.DsiIntegrity is not null)
        {
            throw new InvalidDataException("A DSi integrity policy cannot be applied to an original-DS workspace.");
        }
        if (arguments.DsIntegrity is not null)
        {
            if (builder.DsMetadata is null) { throw new InvalidDataException("A late-DS integrity policy requires a late-DS workspace header."); }
            builder.DsMetadata.Integrity = arguments.DsIntegrity == "preserve" ? NdsDsIntegrityOptions.PreserveStored : NdsDsIntegrityOptions.Unauthenticated;
        }
    }
}
