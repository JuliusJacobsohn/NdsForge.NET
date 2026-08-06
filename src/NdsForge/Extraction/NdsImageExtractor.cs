using System.Buffers;

namespace NdsForge;

/// <summary>Maps selected image structures to a hardened, portable directory tree using atomic per-file writes.</summary>
internal sealed class NdsImageExtractor
{
    /// <summary>Rejects the cross-platform superset of Windows-reserved punctuation before host path construction.</summary>
    private static readonly SearchValues<char> PortableInvalidNameCharacters = SearchValues.Create("<>:\"/\\|?*");
    /// <summary>Supplies parsed regions and keeps their underlying source alive for streamed copies.</summary>
    private readonly NdsImage _image;
    /// <summary>Controls components, overwrite behavior, and optional named-file filtering.</summary>
    private readonly NdsExtractionOptions _options;
    /// <summary>Canonical host destination used as the prefix boundary for every resolved output.</summary>
    private readonly string _root;
    /// <summary>Counts files successfully moved from temporary names into final locations.</summary>
    private int _writtenFiles;
    /// <summary>Counts existing targets intentionally retained under the skip policy.</summary>
    private int _skippedFiles;
    /// <summary>Accumulates logical payload bytes committed to final files, excluding skipped output.</summary>
    private long _writtenBytes;

    /// <summary>Captures an absolute destination root while deferring filesystem mutation until extraction starts.</summary>
    /// <param name="image">Live parsed source whose regions are streamed on demand.</param>
    /// <param name="destination">Host directory interpreted relative to the current process only once.</param>
    /// <param name="options">Immutable extraction selection and collision policy.</param>
    public NdsImageExtractor(NdsImage image, string destination, NdsExtractionOptions options)
    {
        _image = image;
        _options = options;
        _root = Path.GetFullPath(destination);
    }

    /// <summary>Exports selected top-level components in stable order, followed by filtered NitroFS files in file-ID order.</summary>
    /// <param name="cancellationToken">Stops before or during atomic file creation; completed files remain valid.</param>
    /// <returns>Counts of committed, skipped, and committed-byte output.</returns>
    public async ValueTask<NdsExtractionResult> ExtractAsync(CancellationToken cancellationToken)
    {
        EnsureSafeDirectory(_root, create: true);
        if (Includes(NdsImageComponent.Header))
        {
            await WriteMemoryAsync("header.bin", _image.Header.RawData, cancellationToken).ConfigureAwait(false);
        }

        if (Includes(NdsImageComponent.Logo))
        {
            await WriteMemoryAsync("logo.bin", _image.Header.RawData.Slice(0xC0, 156), cancellationToken).ConfigureAwait(false);
        }

        if (Includes(NdsImageComponent.Programs))
        {
            await WriteRegionAsync("arm9.bin", _image.Header.Arm9.CompleteData, cancellationToken).ConfigureAwait(false);
            await WriteRegionAsync("arm7.bin", _image.Header.Arm7.Data, cancellationToken).ConfigureAwait(false);
            if (_image.Header.Arm9i is not null)
            {
                await WriteRegionAsync("arm9i.bin", _image.Header.Arm9i.Data, cancellationToken).ConfigureAwait(false);
            }

            if (_image.Header.Arm7i is not null)
            {
                await WriteRegionAsync("arm7i.bin", _image.Header.Arm7i.Data, cancellationToken).ConfigureAwait(false);
            }
        }

        if (Includes(NdsImageComponent.FileSystemTables))
        {
            await WriteRegionAsync("tables/fnt.bin", _image.Header.FileNameTable, cancellationToken).ConfigureAwait(false);
            await WriteRegionAsync("tables/fat.bin", _image.Header.FileAllocationTable, cancellationToken).ConfigureAwait(false);
        }

        if (Includes(NdsImageComponent.Banner) && _image.Banner is not null)
        {
            await WriteMemoryAsync("banner.bin", _image.Banner.RawData, cancellationToken).ConfigureAwait(false);
        }

        if (Includes(NdsImageComponent.Overlays))
        {
            await ExtractOverlaysAsync(cancellationToken).ConfigureAwait(false);
        }

        if (Includes(NdsImageComponent.NitroFileSystem))
        {
            foreach (NdsFile file in _image.FileSystem.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_options.FileFilter is null || _options.FileFilter(file))
                {
                    await WriteRegionAsync("data" + file.FullPath, file.Data, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return new(_writtenFiles, _skippedFiles, _writtenBytes);
    }

    /// <summary>Exports both raw overlay tables and each resolvable payload with processor, overlay ID, and FAT ID in its name.</summary>
    /// <param name="cancellationToken">Cancels between records or during region streaming.</param>
    private async ValueTask ExtractOverlaysAsync(CancellationToken cancellationToken)
    {
        await WriteRegionAsync(
            "tables/arm9-overlays.bin",
            _image.Header.Arm9OverlayTable,
            cancellationToken).ConfigureAwait(false);
        await WriteRegionAsync(
            "tables/arm7-overlays.bin",
            _image.Header.Arm7OverlayTable,
            cancellationToken).ConfigureAwait(false);

        foreach (NdsOverlay overlay in _image.Arm9Overlays.Concat(_image.Arm7Overlays))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (overlay.Data is null)
            {
                continue;
            }

            string processor = overlay.Processor == NdsProcessor.Arm9 ? "arm9" : "arm7";
            string filename = FormattableString.Invariant(
                $"overlays/{processor}/overlay_{overlay.Id:D4}_file_{overlay.FileId:D5}.bin");
            await WriteRegionAsync(filename, overlay.Data.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Commits already materialized metadata bytes under the same safety and overwrite rules as streamed regions.</summary>
    /// <param name="relativePath">Library-controlled portable output path below the root.</param>
    /// <param name="data">Immutable source bytes such as a header or banner.</param>
    /// <param name="cancellationToken">Cancels the temporary write before final rename.</param>
    private async ValueTask WriteMemoryAsync(
        string relativePath,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        string? output = PrepareOutput(relativePath);
        if (output is null)
        {
            return;
        }

        await WriteAtomicallyAsync(
            output,
            async (stream, token) => await stream.WriteAsync(data, token).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
        _writtenFiles++;
        _writtenBytes += data.Length;
    }

    /// <summary>Streams a bounded image region to disk without allocating its complete contents.</summary>
    /// <param name="relativePath">Portable output path derived from a component or validated NitroFS path.</param>
    /// <param name="region">Previously validated source interval.</param>
    /// <param name="cancellationToken">Cancels copying and removes the incomplete temporary file.</param>
    private async ValueTask WriteRegionAsync(
        string relativePath,
        NdsRegion region,
        CancellationToken cancellationToken)
    {
        string? output = PrepareOutput(relativePath);
        if (output is null)
        {
            return;
        }

        await WriteAtomicallyAsync(
            output,
            async (destination, token) =>
            {
                using Stream source = _image.OpenRead(region);
                await source.CopyToAsync(destination, token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
        _writtenFiles++;
        _writtenBytes += region.Length;
    }

    /// <summary>Validates every segment, proves containment, creates safe parents, and applies the existing-file policy.</summary>
    /// <param name="relativePath">Logical slash-delimited output path that must name a file beneath the root.</param>
    /// <returns>An absolute safe target, or <see langword="null"/> when skip policy retains an existing file.</returns>
    private string? PrepareOutput(string relativePath)
    {
        string[] segments = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new InvalidDataException("An extraction target has no filename.");
        }

        foreach (string segment in segments)
        {
            ValidatePortableName(segment);
        }

        string output = Path.GetFullPath(Path.Combine([_root, .. segments]));
        string rootPrefix = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;
        if (!output.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Extraction path '{relativePath}' escapes the destination root.");
        }

        string directory = Path.GetDirectoryName(output)!;
        EnsureSafeDirectory(directory, create: true);
        if (!File.Exists(output))
        {
            return output;
        }

        RejectReparsePoint(output);
        return _options.OverwritePolicy switch
        {
            NdsOverwritePolicy.Fail => throw new IOException($"Extraction target already exists: {output}"),
            NdsOverwritePolicy.Overwrite => output,
            NdsOverwritePolicy.Skip => Skip(),
            _ => throw new InvalidOperationException("The extraction overwrite policy is invalid."),
        };

        string? Skip()
        {
            _skippedFiles++;
            return null;
        }
    }

    /// <summary>Writes beside the target under a unique name, flushes it, then performs one final move or replacement.</summary>
    /// <param name="output">Validated absolute regular-file target.</param>
    /// <param name="write">Producer that writes complete contents to a newly created exclusive stream.</param>
    /// <param name="cancellationToken">Cancels production or flushing; cleanup removes the temporary path.</param>
    private async ValueTask WriteAtomicallyAsync(
        string output,
        Func<FileStream, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        string temporary = output + ".ndsforge-" + Guid.NewGuid().ToString("N");
        try
        {
            var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (stream.ConfigureAwait(false))
            {
                await write(stream, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, output, _options.OverwritePolicy == NdsOverwritePolicy.Overwrite);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>Walks from the nearest existing ancestor and rejects reparse points before and after creating each missing level.</summary>
    /// <param name="directory">Absolute directory path previously proven beneath the extraction root.</param>
    /// <param name="create">Whether missing levels should be created after ancestor validation.</param>
    private static void EnsureSafeDirectory(string directory, bool create)
    {
        string? parent = directory;
        var missing = new Stack<string>();
        while (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
        {
            missing.Push(parent);
            parent = Path.GetDirectoryName(parent);
        }

        if (!string.IsNullOrEmpty(parent))
        {
            RejectReparsePoint(parent);
        }

        if (!create)
        {
            return;
        }

        while (missing.TryPop(out string? path))
        {
            Directory.CreateDirectory(path);
            RejectReparsePoint(path);
        }
    }

    /// <summary>Prevents junctions and symbolic links from redirecting extraction after lexical containment checks.</summary>
    /// <param name="path">Existing file or directory whose host attributes are inspected.</param>
    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Extraction refuses to traverse reparse point: {path}");
        }
    }

    /// <summary>Rejects traversal, control characters, ambiguous trailing characters, and reserved punctuation across major hosts.</summary>
    /// <param name="name">One NitroFS or library-generated path segment, never a complete path.</param>
    private static void ValidatePortableName(string name)
    {
        if (name is "." or ".." ||
            name.Length == 0 ||
            name.EndsWith(' ') ||
            name.EndsWith('.') ||
            name.AsSpan().ContainsAny(PortableInvalidNameCharacters) ||
            name.Any(static character => char.IsControl(character)))
        {
            throw new InvalidDataException($"NitroFS name '{name}' is unsafe to extract portably.");
        }
    }

    /// <summary>Tests a single component flag against the immutable selection captured for this extraction.</summary>
    /// <param name="component">One atomic export group rather than a composite mask.</param>
    /// <returns><see langword="true"/> when the group participates in this run.</returns>
    private bool Includes(NdsImageComponent component) => (_options.Components & component) != 0;
}
