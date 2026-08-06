using System.Security.Cryptography;
using System.Text.Json;

namespace NdsForge.Corpus;

/// <summary>Builds resumable per-ROM ndstool evidence while limiting temporary proprietary duplication to one image at a time.</summary>
internal static class CorpusOracleGenerator
{
    /// <summary>Runs every asserted differential operation and atomically stores compact oracle JSON.</summary>
    /// <param name="libraryPath">Canonical private ROM directory produced by the catalog command.</param>
    /// <param name="oraclePath">Ignored destination for one JSON record per distinct image.</param>
    /// <param name="ndstoolPath">Historical executable used as the behavioral oracle.</param>
    public static async Task GenerateAsync(string libraryPath, string oraclePath, string ndstoolPath)
    {
        string library = Path.GetFullPath(libraryPath);
        string oracle = Path.GetFullPath(oraclePath);
        string ndstool = Path.GetFullPath(ndstoolPath);
        if (!File.Exists(ndstool))
        {
            throw new FileNotFoundException("The ndstool executable was not found.", ndstool);
        }

        string catalogPath = Path.Combine(Directory.GetParent(library)?.FullName ?? library, "catalog.json");
        CorpusCatalog catalog = await CorpusJsonFiles.ReadAsync(
            catalogPath,
            CorpusJsonContext.Default.CorpusCatalog).ConfigureAwait(false);
        string ndstoolHash = await ComputeSha256Async(ndstool).ConfigureAwait(false);
        Directory.CreateDirectory(oracle);
        int index = 0;
        foreach (CorpusRom rom in catalog.Roms)
        {
            index++;
            string source = Path.Combine(library, rom.FileName);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException($"Cataloged ROM is missing: {rom.FileName}", source);
            }

            string oracleFile = Path.Combine(oracle, $"{rom.GameCode}-{rom.Sha256[..12]}.json");
            if (await IsCurrentAsync(oracleFile, rom.Sha256, ndstoolHash).ConfigureAwait(false))
            {
                await Console.Out.WriteLineAsync($"[{index}/{catalog.Roms.Count}] current: {rom.FileName}").ConfigureAwait(false);
                continue;
            }

            await Console.Out.WriteLineAsync($"[{index}/{catalog.Roms.Count}] oracle: {rom.FileName}").ConfigureAwait(false);
            string workspace = Path.Combine(Path.GetTempPath(), $"ndsforge-corpus-{Guid.NewGuid():N}");
            Directory.CreateDirectory(workspace);
            try
            {
                IReadOnlyList<OracleOperation> operations = await CorpusOracleOperations.RunAllAsync(
                    ndstool,
                    source,
                    workspace).ConfigureAwait(false);
                var result = new CorpusOracle(1, DateTimeOffset.UtcNow, ndstoolHash, rom, operations);
                await CorpusJsonFiles.WriteAsync(oracleFile, result, CorpusJsonContext.Default.CorpusOracle).ConfigureAwait(false);
            }
            finally
            {
                DeleteWorkspace(workspace);
            }
        }
    }

    /// <summary>Allows an interrupted multi-ROM run to resume only when both input image and executable identities match.</summary>
    /// <param name="path">Existing oracle candidate.</param>
    /// <param name="romHash">Current cataloged image hash.</param>
    /// <param name="ndstoolHash">Current executable hash.</param>
    /// <returns><see langword="true"/> when regeneration would be redundant.</returns>
    private static async Task<bool> IsCurrentAsync(string path, string romHash, string ndstoolHash)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            CorpusOracle oracle = await CorpusJsonFiles.ReadAsync(path, CorpusJsonContext.Default.CorpusOracle).ConfigureAwait(false);
            return oracle.SchemaVersion == 1 &&
                StringComparer.OrdinalIgnoreCase.Equals(oracle.Rom.Sha256, romHash) &&
                StringComparer.OrdinalIgnoreCase.Equals(oracle.NdstoolSha256, ndstoolHash) &&
                oracle.Operations.Count == CorpusOracleOperations.ExpectedOperationCount;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Calculates executable identity without loading it into memory.</summary>
    /// <param name="path">Complete file included in the digest.</param>
    /// <returns>Uppercase SHA-256 hexadecimal text.</returns>
    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream).ConfigureAwait(false));
    }

    /// <summary>Deletes only a GUID-named child proven to remain immediately beneath the process temporary directory.</summary>
    /// <param name="workspace">Disposable directory created by this generator.</param>
    private static void DeleteWorkspace(string workspace)
    {
        string resolved = Path.GetFullPath(workspace);
        string temporary = Path.GetFullPath(Path.GetTempPath());
        string boundary = temporary.EndsWith(Path.DirectorySeparatorChar) ? temporary : temporary + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(boundary, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(resolved).StartsWith("ndsforge-corpus-", StringComparison.Ordinal))
        {
            throw new IOException($"Refusing to delete an unverified corpus workspace: {resolved}");
        }

        if (Directory.Exists(resolved))
        {
            Directory.Delete(resolved, recursive: true);
        }
    }
}
