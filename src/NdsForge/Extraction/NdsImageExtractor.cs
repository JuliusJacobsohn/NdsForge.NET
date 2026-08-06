using System.Buffers;

namespace NdsForge;

internal sealed class NdsImageExtractor
{
    private static readonly SearchValues<char> PortableInvalidNameCharacters = SearchValues.Create("<>:\"/\\|?*");
    private readonly NdsImage _image;
    private readonly NdsExtractionOptions _options;
    private readonly string _root;
    private int _writtenFiles;
    private int _skippedFiles;
    private long _writtenBytes;

    public NdsImageExtractor(NdsImage image, string destination, NdsExtractionOptions options)
    {
        _image = image;
        _options = options;
        _root = Path.GetFullPath(destination);
    }

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

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Extraction refuses to traverse reparse point: {path}");
        }
    }

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

    private bool Includes(NdsImageComponent component) => (_options.Components & component) != 0;
}
