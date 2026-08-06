using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace NdsForge.Corpus;

/// <summary>Derives trustworthy names and catalog records from parsed headers and localized banner content.</summary>
internal static partial class CorpusCatalogBuilder
{
    /// <summary>Recomputes canonical metadata and renames an existing deduplicated library without changing payload bytes.</summary>
    /// <param name="libraryPath">Private flat library whose complete contents are catalog owned.</param>
    /// <param name="catalogPath">Existing mapping used to retain original import names by SHA-256.</param>
    public static async Task RefreshAsync(string libraryPath, string catalogPath)
    {
        string library = Path.GetFullPath(libraryPath);
        CorpusCatalog prior = await CorpusJsonFiles.ReadAsync(
            catalogPath,
            CorpusJsonContext.Default.CorpusCatalog).ConfigureAwait(false);
        Dictionary<string, CorpusRom> priorByFileName = prior.Roms.ToDictionary(
            static rom => rom.FileName,
            StringComparer.OrdinalIgnoreCase);
        var candidates = new List<(string CurrentPath, CorpusRom Prior, CorpusRom Candidate)>();
        foreach (string path in Directory.GetFiles(library, "*.nds", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!priorByFileName.TryGetValue(Path.GetFileName(path), out CorpusRom? old))
            {
                throw new InvalidDataException($"The existing catalog has no row for {Path.GetFileName(path)}.");
            }

            CorpusRom candidate = await InspectAsync(
                library,
                path,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                old.SourceName).ConfigureAwait(false);
            candidates.Add((path, old, candidate with { SourceName = old.SourceName }));
        }

        if (candidates.Count != prior.Roms.Count)
        {
            throw new InvalidDataException("Refusing to refresh a corpus whose image count differs from its catalog.");
        }

        var refreshed = new List<(string CurrentPath, CorpusRom Rom)>(candidates.Count);
        foreach (IGrouping<string, (string CurrentPath, CorpusRom Prior, CorpusRom Candidate)> group in
                 candidates.GroupBy(static item => item.Candidate.FileName, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group.OrderByDescending(static item =>
                    item.Prior.FileName.Equals(item.Candidate.FileName, StringComparison.OrdinalIgnoreCase))
                .ThenBy(static item => item.Candidate.Sha256, StringComparer.Ordinal)
                .ToArray();
            refreshed.Add((ordered[0].CurrentPath, ordered[0].Candidate));
            foreach ((string currentPath, _, CorpusRom candidate) in ordered.Skip(1))
            {
                string stem = Path.GetFileNameWithoutExtension(candidate.FileName);
                refreshed.Add((currentPath, candidate with { FileName = $"{stem} [{candidate.Sha256[..8]}].nds" }));
            }
        }

        foreach ((string currentPath, CorpusRom rom) in refreshed.Where(static item =>
                     !Path.GetFileName(item.CurrentPath).Equals(item.Rom.FileName, StringComparison.OrdinalIgnoreCase)))
        {
            string destination = Path.GetFullPath(Path.Combine(library, rom.FileName));
            if (!destination.StartsWith(library + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || File.Exists(destination))
            {
                throw new IOException($"Unsafe or occupied refreshed corpus destination: {destination}");
            }

            File.Move(currentPath, destination);
            await Console.Out.WriteLineAsync($"renamed: {Path.GetFileName(currentPath)} -> {rom.FileName}").ConfigureAwait(false);
        }

        CorpusRom[] rows = refreshed.Select(static item => item.Rom)
            .OrderBy(static rom => rom.FileName, StringComparer.Ordinal)
            .ToArray();
        await CorpusJsonFiles.WriteAsync(
            catalogPath,
            new CorpusCatalog(1, DateTimeOffset.UtcNow, rows),
            CorpusJsonContext.Default.CorpusCatalog).ConfigureAwait(false);
    }

    /// <summary>Adds recursively discovered images to an existing library while storing each distinct SHA-256 only once.</summary>
    /// <param name="additionalPath">External tree searched recursively without modifying it.</param>
    /// <param name="libraryPath">Existing canonical private library receiving only new content.</param>
    /// <param name="catalogPath">Existing catalog atomically replaced after all copies succeed.</param>
    public static async Task MergeAsync(string additionalPath, string libraryPath, string catalogPath)
    {
        string additional = Path.GetFullPath(additionalPath);
        string library = Path.GetFullPath(libraryPath);
        CorpusCatalog catalog = await CorpusJsonFiles.ReadAsync(
            catalogPath,
            CorpusJsonContext.Default.CorpusCatalog).ConfigureAwait(false);
        var knownHashes = new HashSet<string>(catalog.Roms.Select(static rom => rom.Sha256), StringComparer.OrdinalIgnoreCase);
        var assignedNames = new HashSet<string>(catalog.Roms.Select(static rom => rom.FileName), StringComparer.OrdinalIgnoreCase);
        var additions = new List<(string Source, CorpusRom Rom)>();
        string[] paths = Directory.GetFiles(additional, "*.nds", SearchOption.AllDirectories);
        foreach (string path in paths.Order(StringComparer.OrdinalIgnoreCase))
        {
            CorpusRom rom = await InspectAsync(additional, path, assignedNames).ConfigureAwait(false);
            if (!knownHashes.Add(rom.Sha256))
            {
                await Console.Out.WriteLineAsync($"duplicate {rom.Sha256[..12]}: {rom.SourceName}").ConfigureAwait(false);
                continue;
            }

            additions.Add((path, rom));
            await Console.Out.WriteLineAsync($"new: {rom.SourceName} -> {rom.FileName}").ConfigureAwait(false);
        }

        foreach ((string source, CorpusRom rom) in additions)
        {
            string destination = Path.Combine(library, rom.FileName);
            if (File.Exists(destination))
            {
                throw new IOException($"New canonical destination unexpectedly exists: {destination}");
            }

            File.Copy(source, destination);
        }

        CorpusRom[] combined = catalog.Roms.Concat(additions.Select(static addition => addition.Rom))
            .OrderBy(static rom => rom.FileName, StringComparer.Ordinal)
            .ToArray();
        await CorpusJsonFiles.WriteAsync(
            catalogPath,
            new CorpusCatalog(1, DateTimeOffset.UtcNow, combined),
            CorpusJsonContext.Default.CorpusCatalog).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(
            $"Merged {additions.Count} new images; skipped {paths.Length - additions.Count} byte-identical duplicates; total {combined.Length}.")
            .ConfigureAwait(false);
    }

    /// <summary>Catalogs all incoming images before moving them, preventing a parse failure from producing a partial library.</summary>
    /// <param name="incomingPath">Directory containing copied, untrusted filenames.</param>
    /// <param name="libraryPath">Ignored private directory receiving canonical names.</param>
    /// <param name="catalogPath">Ignored JSON mapping retained beside the corpus.</param>
    public static async Task BuildAsync(string incomingPath, string libraryPath, string catalogPath)
    {
        string incoming = Path.GetFullPath(incomingPath);
        string library = Path.GetFullPath(libraryPath);
        string[] paths = Directory.GetFiles(incoming, "*.nds", SearchOption.AllDirectories);
        if (paths.Length == 0)
        {
            throw new InvalidDataException($"No .nds files were found beneath {incoming}.");
        }

        var pending = new List<(string Source, CorpusRom Rom)>();
        var assignedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths.Order(StringComparer.OrdinalIgnoreCase))
        {
            CorpusRom rom = await InspectAsync(incoming, path, assignedNames).ConfigureAwait(false);
            pending.Add((path, rom));
            await Console.Out.WriteLineAsync($"{Path.GetFileName(path)} -> {rom.FileName}").ConfigureAwait(false);
        }

        Directory.CreateDirectory(library);
        foreach ((string source, CorpusRom rom) in pending)
        {
            string destination = Path.Combine(library, rom.FileName);
            if (File.Exists(destination))
            {
                string existingHash = await ComputeSha256Async(destination).ConfigureAwait(false);
                if (!StringComparer.OrdinalIgnoreCase.Equals(existingHash, rom.Sha256))
                {
                    throw new IOException($"Canonical destination already contains different bytes: {destination}");
                }

                File.Delete(source);
            }
            else
            {
                File.Move(source, destination);
            }
        }

        var catalog = new CorpusCatalog(1, DateTimeOffset.UtcNow, pending.Select(static item => item.Rom).ToArray());
        await CorpusJsonFiles.WriteAsync(catalogPath, catalog, CorpusJsonContext.Default.CorpusCatalog).ConfigureAwait(false);
    }

    /// <summary>Extracts identity, localized title, region, and a full-file digest from one live image.</summary>
    /// <param name="root">Incoming root used to retain a relative original mapping.</param>
    /// <param name="path">Image path parsed without trusting its current filename.</param>
    /// <param name="assignedNames">Case-insensitive set preventing collisions on Windows.</param>
    /// <param name="variantSourceName">Optional original import name retained across later canonical-name refreshes.</param>
    /// <returns>A detached catalog row whose canonical filename is already unique.</returns>
    private static async Task<CorpusRom> InspectAsync(
        string root,
        string path,
        HashSet<string> assignedNames,
        string? variantSourceName = null)
    {
        await using NdsImage image = await NdsImage.OpenAsync(path).ConfigureAwait(false);
        NdsImageManifest manifest = await image.CreateManifestAsync().ConfigureAwait(false);
        (string language, string region) = ResolveLocale(manifest.Header.GameCode);
        var titles = manifest.Banner?.Titles
            .Where(static item => !string.IsNullOrWhiteSpace(item.Value))
            .ToDictionary(static item => item.Key, static item => item.Value, StringComparer.Ordinal) ?? [];
        string selected = titles.TryGetValue(language, out string? localized) ? localized :
            titles.TryGetValue("English", out string? english) ? english : manifest.Header.Title;
        string displayTitle = NormalizeTitle(selected, manifest.Header.Title);
        string variant = ResolveVariant(
            Path.GetFileNameWithoutExtension(variantSourceName ?? path),
            selected);
        string stem = SanitizeFileName($"{displayTitle} [{manifest.Header.GameCode}] [{region}]" +
            (variant.Length == 0 ? string.Empty : $" [{variant}]"));
        string fileName = stem + ".nds";
        if (!assignedNames.Add(fileName))
        {
            fileName = $"{stem} [{manifest.ImageSha256[..8]}].nds";
            if (!assignedNames.Add(fileName))
            {
                throw new InvalidDataException($"Duplicate corpus identity remains ambiguous for {path}.");
            }
        }

        return new(
            Path.GetRelativePath(root, path).Replace('\\', '/'),
            fileName,
            manifest.PhysicalLength,
            manifest.ImageSha256,
            manifest.Header.Title,
            manifest.Header.GameCode,
            manifest.Header.MakerCode,
            manifest.Header.Kind,
            manifest.Header.Version,
            region,
            language,
            displayTitle,
            titles);
    }

    /// <summary>Maps the conventional fourth game-code character to a preferred banner slot and useful region label.</summary>
    /// <param name="gameCode">Four-character cartridge product identity.</param>
    /// <returns>Banner language key and human-readable distribution locale.</returns>
    private static (string Language, string Region) ResolveLocale(string gameCode) => gameCode.Length == 4
        ? gameCode[3] switch
        {
            'J' => ("Japanese", "Japan - Japanese"),
            'E' => ("English", "USA - English"),
            'P' => ("English", "Europe - Multilingual"),
            'D' => ("German", "Germany - German"),
            'F' => ("French", "France - French"),
            'I' => ("Italian", "Italy - Italian"),
            'S' => ("Spanish", "Spain - Spanish"),
            'K' => ("Korean", "Korea - Korean"),
            'C' => ("Chinese", "China - Chinese"),
            'O' => ("English", "USA - English"),
            _ => ("English", $"Region {gameCode[3]}"),
        }
        : ("English", "Unknown region");

    /// <summary>Turns multiline banner presentation text into a concise title while removing known publisher credits.</summary>
    /// <param name="bannerTitle">Localized native banner text, potentially split across three display lines.</param>
    /// <param name="fallback">Header title used when all banner lines are blank or credits.</param>
    /// <returns>Single-line title suitable for a host filename.</returns>
    private static string NormalizeTitle(string bannerTitle, string fallback)
    {
        string[] lines = bannerTitle.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static line => line is not "Nintendo" and not "Staro")
            .Select(static line => line.TrimEnd(' ', ':', '-', '—'))
            .ToArray();
        return lines.Length == 0 ? fallback.Trim() : string.Join(" - ", lines);
    }

    /// <summary>Retains meaningful randomized or modified provenance that cannot be inferred from cartridge metadata.</summary>
    /// <param name="sourceStem">Original user filename used only for variant classification.</param>
    /// <param name="bannerTitle">Banner text used to recognize an explicit mod author credit.</param>
    /// <returns>Short variant label, or empty text for an apparently ordinary image.</returns>
    private static string ResolveVariant(string sourceStem, string bannerTitle)
    {
        if (sourceStem.Contains("modded", StringComparison.OrdinalIgnoreCase))
        {
            return bannerTitle.Contains("Staro", StringComparison.OrdinalIgnoreCase) ? "Staro Mod" : "Modded";
        }

        if (!sourceStem.Contains("randomized", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (sourceStem.Contains("starters", StringComparison.OrdinalIgnoreCase))
        {
            return "Randomized Starters";
        }

        Match seed = SeedSuffix().Match(sourceStem);
        return seed.Success ? $"Randomized {seed.Value}" : "Randomized";
    }

    /// <summary>Removes Windows-reserved filename characters and collapses whitespace without losing Unicode titles.</summary>
    /// <param name="value">Proposed descriptive stem.</param>
    /// <returns>Portable non-empty filename stem.</returns>
    private static string SanitizeFileName(string value)
    {
        string cleaned = InvalidFileCharacters().Replace(value, "-");
        cleaned = RepeatedWhitespace().Replace(cleaned, " ").Trim(' ', '.');
        return cleaned.Length == 0 ? "Untitled Nintendo DS Image" : cleaned;
    }

    /// <summary>Streams a file digest so cataloging never duplicates a large ROM in managed memory.</summary>
    /// <param name="path">Complete file included in the identity check.</param>
    /// <returns>Uppercase SHA-256 hexadecimal text.</returns>
    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous);
        byte[] hash = await SHA256.HashDataAsync(stream).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    /// <summary>Recognizes date or seed suffixes long enough to distinguish randomized variants.</summary>
    [GeneratedRegex(@"\d{6,}", RegexOptions.CultureInvariant)]
    private static partial Regex SeedSuffix();

    /// <summary>Recognizes characters forbidden by Windows plus colon, which is not returned consistently across platforms.</summary>
    [GeneratedRegex("[<>:\"/\\\\|?*]", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidFileCharacters();

    /// <summary>Collapses presentation whitespace introduced by multiline banner text.</summary>
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex RepeatedWhitespace();
}
