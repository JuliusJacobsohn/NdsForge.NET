namespace NdsForge;

/// <summary>Reopens generated output through public parsing paths and compares every recipe-owned payload.</summary>
internal static class NdsImageBuildVerifier
{
    /// <summary>Proves checksums, paths, File IDs, Overlay records, Regions, and payload bytes agree with the recipe.</summary>
    /// <param name="destination">Completed readable stream left open by the loader.</param>
    /// <param name="builder">Expected Overlay definitions and payload bytes.</param>
    /// <param name="fileSystem">Frozen expected path and named payload order.</param>
    /// <param name="cancellationToken">Cancels reopen parsing or payload comparisons.</param>
    public static async ValueTask VerifyAsync(
        Stream destination,
        NdsImageBuilder builder,
        NdsFileSystemBuildSnapshot fileSystem,
        CancellationToken cancellationToken)
    {
        destination.Position = 0;
        using NdsImage image = await NdsImage.OpenAsync(
            destination,
            leaveOpen: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        NdsValidationResult validation = image.Validate();
        if (!validation.IsValid)
        {
            throw new InvalidDataException(
                $"Generated image verification failed: {string.Join("; ", validation.Diagnostics.Select(static item => item.Message))}");
        }

        for (int fileId = 0; fileId < fileSystem.FilesInIdOrder.Count; fileId++)
        {
            byte[] actual = await image.FileSystem.GetFile(fileId).ReadAllBytesAsync(cancellationToken).ConfigureAwait(false);
            if (!actual.AsSpan().SequenceEqual(fileSystem.FilesInIdOrder[fileId].Contents.Span))
            {
                throw new InvalidDataException($"Generated image payload verification failed for File ID {fileId}.");
            }
        }

        await VerifyOverlaysAsync(image, image.Arm9Overlays, builder.Arm9Overlays, cancellationToken).ConfigureAwait(false);
        await VerifyOverlaysAsync(image, image.Arm7Overlays, builder.Arm7Overlays, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Byte-compares private Overlay Allocations and verifies record identity after production parsing.</summary>
    /// <param name="image">Reopened output used for bounded payload streams.</param>
    /// <param name="actual">Parsed records from one processor table.</param>
    /// <param name="expected">Recipe definitions in the same insertion order.</param>
    /// <param name="cancellationToken">Cancels payload materialization.</param>
    private static async ValueTask VerifyOverlaysAsync(
        NdsImage image,
        IReadOnlyList<NdsOverlay> actual,
        IReadOnlyList<NdsOverlayDefinition> expected,
        CancellationToken cancellationToken)
    {
        if (actual.Count != expected.Count)
        {
            throw new InvalidDataException("Generated Overlay table entry count did not round-trip.");
        }

        for (int index = 0; index < actual.Count; index++)
        {
            if (actual[index].Id != expected[index].Id || actual[index].Data is null)
            {
                throw new InvalidDataException($"Generated Overlay record {index} did not round-trip.");
            }

            if (expected[index].LinkedFilePath is not null)
            {
                if (actual[index].File?.FullPath != expected[index].LinkedFilePath)
                {
                    throw new InvalidDataException($"Generated Overlay file link {index} did not round-trip.");
                }

                continue;
            }

            using Stream stream = image.OpenRead(actual[index].Data!.Value);
            byte[] contents = new byte[expected[index].Contents.Length];
            await stream.ReadExactlyAsync(contents, cancellationToken).ConfigureAwait(false);
            if (!contents.AsSpan().SequenceEqual(expected[index].Contents.Span))
            {
                throw new InvalidDataException($"Generated Overlay payload {index} did not round-trip.");
            }
        }
    }
}
