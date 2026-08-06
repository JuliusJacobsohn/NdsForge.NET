using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace NdsForge.Corpus;

/// <summary>Commits generated JSON atomically so an interrupted corpus run cannot masquerade as a complete oracle.</summary>
internal static class CorpusJsonFiles
{
    /// <summary>Reads a complete generated contract with its source-generated metadata.</summary>
    /// <typeparam name="T">Expected root contract type.</typeparam>
    /// <param name="path">Existing JSON document.</param>
    /// <param name="typeInfo">Source-generated deserialization metadata.</param>
    /// <returns>Non-null validated JSON root; semantic checks remain the caller's responsibility.</returns>
    public static async Task<T> ReadAsync<T>(string path, JsonTypeInfo<T> typeInfo)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        return await JsonSerializer.DeserializeAsync(stream, typeInfo).ConfigureAwait(false) ??
            throw new InvalidDataException($"Generated JSON contained no {typeof(T).Name}: {path}");
    }

    /// <summary>Serializes beside the destination and moves the complete document into place only after success.</summary>
    /// <typeparam name="T">Source-generated contract type.</typeparam>
    /// <param name="path">Destination JSON file whose parent is created when necessary.</param>
    /// <param name="value">Complete detached value.</param>
    /// <param name="typeInfo">Source-generated serialization metadata.</param>
    public static async Task WriteAsync<T>(string path, T value, JsonTypeInfo<T> typeInfo)
    {
        string destination = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
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
                await JsonSerializer.SerializeAsync(stream, value, typeInfo).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }

            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
