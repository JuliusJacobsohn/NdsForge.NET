namespace NdsForge;

/// <summary>Detaches logical DS components from an existing Image for deliberate Structural Rebuild operations.</summary>
internal static class NdsImageBuildImporter
{
    /// <summary>Materializes every referenced payload and reconstructs Overlay sharing through named NitroFS links.</summary>
    /// <param name="image">Live parsed source whose random-access streams remain valid for the duration of import.</param>
    /// <param name="cancellationToken">Cancels component reads without publishing an incomplete builder.</param>
    /// <returns>A fully detached Build Recipe preserving logical identities supported by the deterministic writer.</returns>
    public static async ValueTask<NdsImageBuilder> ImportAsync(
        NdsImage image,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Header.Kind != NdsImageKind.NintendoDs)
        {
            throw new NotSupportedException("DSi structural import requires the DSi build pipeline and cannot be downgraded to DS.");
        }

        byte[] arm9 = await ReadRegionAsync(image, image.Header.Arm9.Data, cancellationToken).ConfigureAwait(false);
        byte[] arm7 = await ReadRegionAsync(image, image.Header.Arm7.Data, cancellationToken).ConfigureAwait(false);
        var arm9Definition = new NdsProgramDefinition(
            NdsProcessor.Arm9,
            arm9,
            image.Header.Arm9.LoadAddress,
            image.Header.Arm9.EntryAddress);
        if (image.Header.Arm9.Footer is not null)
        {
            arm9Definition.SetFooter(
                await ReadRegionAsync(image, image.Header.Arm9.Footer.Value, cancellationToken).ConfigureAwait(false));
        }

        var builder = new NdsImageBuilder
        {
            Title = image.Header.Title,
            GameCode = image.Header.GameCode,
            MakerCode = image.Header.MakerCode,
            Version = image.Header.Version,
            EncryptionSeedSelect = image.Header.EncryptionSeedSelect,
            RegionCode = image.Header.RegionCode,
            AutoStart = image.Header.AutoStart,
            NormalCardControl = image.Header.NormalCardControl,
            SecureCardControl = image.Header.SecureCardControl,
            SecureTransferTimeout = image.Header.SecureTransferTimeout,
            Arm9AutoLoad = image.Header.Arm9AutoLoad,
            Arm7AutoLoad = image.Header.Arm7AutoLoad,
            SecureDisable = image.Header.SecureDisable,
            Arm9 = arm9Definition,
            Arm7 = new(
                NdsProcessor.Arm7,
                arm7,
                image.Header.Arm7.LoadAddress,
                image.Header.Arm7.EntryAddress),
            Banner = image.Banner,
        };
        builder.SetNintendoLogo(image.Header.RawData.Span.Slice(0xC0, 156));

        foreach (NdsDirectory directory in image.FileSystem.Directories)
        {
            if (directory.Parent is not null)
            {
                builder.FileSystem.CreateDirectory(directory.FullPath);
            }
        }

        foreach (NdsFile file in image.FileSystem.Files)
        {
            builder.FileSystem.AddFile(
                file.FullPath,
                await file.ReadAllBytesAsync(cancellationToken).ConfigureAwait(false));
        }

        var importedPrivateFileIds = new HashSet<uint>();
        foreach (NdsOverlay overlay in image.Arm9Overlays.Concat(image.Arm7Overlays))
        {
            builder.AddOverlay(await ImportOverlayAsync(
                image,
                overlay,
                builder.FileSystem,
                importedPrivateFileIds,
                cancellationToken).ConfigureAwait(false));
        }

        return builder;
    }

    /// <summary>Preserves named sharing or copies one private Allocation while rejecting ambiguous shared-private topology.</summary>
    /// <param name="image">Source used to read private payload Regions.</param>
    /// <param name="overlay">Parsed record containing both runtime and FAT identities.</param>
    /// <param name="fileSystem">Detached tree supplying stable builder-owned file objects for named links.</param>
    /// <param name="importedPrivateFileIds">Detects multiple records sharing an unnamed Allocation not yet representable by the recipe.</param>
    /// <param name="cancellationToken">Cancels private payload reads.</param>
    /// <returns>A linked or private Overlay definition with identical table metadata.</returns>
    private static async ValueTask<NdsOverlayDefinition> ImportOverlayAsync(
        NdsImage image,
        NdsOverlay overlay,
        NdsFileSystemBuilder fileSystem,
        HashSet<uint> importedPrivateFileIds,
        CancellationToken cancellationToken)
    {
        if (overlay.File is not null)
        {
            return NdsOverlayDefinition.LinkToFile(
                overlay.Processor,
                overlay.Id,
                fileSystem.GetFile(overlay.File.FullPath),
                overlay.LoadAddress,
                overlay.RamSize,
                overlay.BssSize,
                overlay.StaticInitializerStart,
                overlay.StaticInitializerEnd,
                overlay.CompressedSize,
                overlay.Flags);
        }

        if (overlay.Data is null)
        {
            throw new InvalidDataException($"Overlay {overlay.Id} references missing File ID {overlay.FileId}.");
        }

        if (!importedPrivateFileIds.Add(overlay.FileId))
        {
            throw new NotSupportedException("Multiple Overlays sharing one unnamed Allocation require an explicit shared-allocation recipe.");
        }

        return new(
            overlay.Processor,
            overlay.Id,
            await ReadRegionAsync(image, overlay.Data.Value, cancellationToken).ConfigureAwait(false),
            overlay.LoadAddress,
            overlay.RamSize,
            overlay.BssSize,
            overlay.StaticInitializerStart,
            overlay.StaticInitializerEnd,
            overlay.CompressedSize,
            overlay.Flags);
    }

    /// <summary>Materializes one validated Region through the public bounded-stream abstraction.</summary>
    /// <param name="image">Source Image retaining ownership of its data source.</param>
    /// <param name="region">Physical interval already validated during parsing.</param>
    /// <param name="cancellationToken">Cancels allocation-filling reads.</param>
    /// <returns>An independently owned exact byte copy.</returns>
    private static async ValueTask<byte[]> ReadRegionAsync(
        NdsImage image,
        NdsRegion region,
        CancellationToken cancellationToken)
    {
        if (region.Length > Array.MaxLength)
        {
            throw new IOException("A build component is too large to materialize in a detached recipe.");
        }

        using Stream stream = image.OpenRead(region);
        byte[] data = new byte[region.Length];
        await stream.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false);
        return data;
    }
}
