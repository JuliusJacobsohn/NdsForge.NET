using System.Text.Json;
using System.Text.Json.Serialization;

namespace NdsForge;

/// <summary>
/// Provides a stable, content-addressed description of one parsed image for CI artifacts, review, and semantic
/// comparison. The manifest contains no ROM payload bytes and cannot reconstruct copyrighted or private content.
/// </summary>
public sealed class NdsImageManifest
{
    /// <summary>Identifies the currently supported JSON contract and prevents silent interpretation of future shapes.</summary>
    public const int CurrentSchemaVersion = 1;
    /// <summary>Retains deterministic enum strings and conservative JSON parsing rules shared by all manifest operations.</summary>
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions(indented: false);

    /// <summary>Records the contract version required to interpret every other field.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    /// <summary>Records physical source bytes independently from header claims and nominal cartridge capacity.</summary>
    public long PhysicalLength { get; init; }
    /// <summary>Hashes every physical image byte so padding-only changes remain detectable.</summary>
    public string ImageSha256 { get; init; } = string.Empty;
    /// <summary>Contains common typed header values plus a hash covering reserved header bytes.</summary>
    public NdsManifestHeader Header { get; init; } = new();
    /// <summary>Contains extended DSi metadata, or remains absent for an original DS image.</summary>
    public NdsManifestDsi? Dsi { get; init; }
    /// <summary>Contains executable snapshots in processor enumeration order.</summary>
    public IReadOnlyList<NdsManifestProgram> Programs { get; init; } = [];
    /// <summary>Contains every NitroFS directory path, including the root and explicitly empty nodes.</summary>
    public IReadOnlyList<string> Directories { get; init; } = [];
    /// <summary>Contains every named NitroFS entry in canonical ordinal path order.</summary>
    public IReadOnlyList<NdsManifestFile> Files { get; init; } = [];
    /// <summary>Contains every FAT record in numeric File ID order, including unnamed allocations.</summary>
    public IReadOnlyList<NdsManifestAllocation> Allocations { get; init; } = [];
    /// <summary>Contains ARM9 then ARM7 Overlay records ordered by runtime Overlay ID.</summary>
    public IReadOnlyList<NdsManifestOverlay> Overlays { get; init; } = [];
    /// <summary>Contains native menu metadata, or remains absent when the image declares no supported banner.</summary>
    public NdsManifestBanner? Banner { get; init; }

    /// <summary>Captures hashes and structured metadata from a live image without taking ownership of it.</summary>
    /// <param name="image">Parsed image that must remain undisposed until asynchronous hashing completes.</param>
    /// <param name="cancellationToken">Cancels between or during region hashes.</param>
    /// <returns>A detached manifest safe to serialize after the image is disposed.</returns>
    public static ValueTask<NdsImageManifest> CaptureAsync(
        NdsImage image,
        CancellationToken cancellationToken = default) =>
        NdsImageManifestCapture.CaptureAsync(image, cancellationToken);

    /// <summary>Serializes this contract with enum names and deterministic property order suitable for source review.</summary>
    /// <param name="indented">Whether human-facing whitespace is included.</param>
    /// <returns>UTF-16 managed text containing UTF-8-compatible JSON characters.</returns>
    public string ToJson(bool indented = true)
    {
        Validate();
        return JsonSerializer.Serialize(this, CreateJsonOptions(indented));
    }

    /// <summary>Parses a complete JSON contract and rejects unknown schema versions or incomplete required identity.</summary>
    /// <param name="json">Manifest JSON produced by this schema or an equivalent serializer.</param>
    /// <returns>A validated detached manifest.</returns>
    public static NdsImageManifest ParseJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        NdsImageManifest manifest = JsonSerializer.Deserialize<NdsImageManifest>(json, JsonOptions) ??
            throw new InvalidDataException("The JSON document did not contain an NDS image manifest.");
        manifest.Validate();
        return manifest;
    }

    /// <summary>Writes one UTF-8 JSON document at the destination's current position without closing the stream.</summary>
    /// <param name="destination">Writable caller-owned stream.</param>
    /// <param name="indented">Whether human-facing whitespace is included.</param>
    /// <param name="cancellationToken">Cancels JSON serialization and stream output.</param>
    public async ValueTask WriteJsonAsync(
        Stream destination,
        bool indented = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The manifest destination must be writable.", nameof(destination));
        }

        Validate();
        await JsonSerializer.SerializeAsync(
            destination,
            this,
            CreateJsonOptions(indented),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one UTF-8 JSON document without closing the caller-owned source stream.</summary>
    /// <param name="source">Readable stream positioned at the manifest document.</param>
    /// <param name="cancellationToken">Cancels parsing before a validated contract is returned.</param>
    /// <returns>A validated detached manifest.</returns>
    public static async ValueTask<NdsImageManifest> ReadJsonAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("The manifest source must be readable.", nameof(source));
        }

        NdsImageManifest manifest = await JsonSerializer.DeserializeAsync<NdsImageManifest>(
            source,
            JsonOptions,
            cancellationToken).ConfigureAwait(false) ??
            throw new InvalidDataException("The JSON document did not contain an NDS image manifest.");
        manifest.Validate();
        return manifest;
    }

    /// <summary>Enforces schema, scalar, collection, and SHA-256 invariants after capture or untrusted JSON parsing.</summary>
    internal void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion || PhysicalLength < 0 || Header is null ||
            Programs is null || Directories is null || Files is null || Allocations is null || Overlays is null)
        {
            throw new InvalidDataException("The NDS image manifest schema or required fields are invalid.");
        }

        ValidateHash(ImageSha256, nameof(ImageSha256));
        ValidateHash(Header.Sha256, "Header.Sha256");
        if (Header.Title is null || Header.GameCode is null || Header.MakerCode is null ||
            !Enum.IsDefined(Header.Kind) ||
            Programs.Any(static value => value is null) ||
            Directories.Any(static value => value is null) ||
            Files.Any(static value => value is null) ||
            Allocations.Any(static value => value is null) ||
            Overlays.Any(static value => value is null))
        {
            throw new InvalidDataException("The manifest contains an invalid image kind or null collection entry.");
        }

        if (Programs.Select(static value => value.Processor).Distinct().Count() != Programs.Count ||
            Programs.Any(static value => !Enum.IsDefined(value.Processor) || value.Offset < 0 || value.Length < 0))
        {
            throw new InvalidDataException("Manifest Programs must have unique processors and non-negative regions.");
        }

        foreach (NdsManifestProgram program in Programs)
        {
            ValidateHash(program.Sha256, $"Programs[{program.Processor}].Sha256");
        }

        if (Directories.Distinct(StringComparer.Ordinal).Count() != Directories.Count ||
            Directories.Any(static value => string.IsNullOrEmpty(value) || !value.StartsWith('/')))
        {
            throw new InvalidDataException("Manifest directories must be unique canonical absolute paths.");
        }

        if (Files.Select(static value => value.Path).Distinct(StringComparer.Ordinal).Count() != Files.Count ||
            Files.Any(static value => string.IsNullOrWhiteSpace(value.Path) || value.FileId < 0 || value.Offset < 0 || value.Length < 0))
        {
            throw new InvalidDataException("Manifest files must have unique paths, valid File IDs, and non-negative regions.");
        }

        foreach (NdsManifestFile file in Files)
        {
            ValidateHash(file.Sha256, $"Files[{file.Path}].Sha256");
        }

        if (Allocations.Select(static value => value.FileId).Distinct().Count() != Allocations.Count ||
            Allocations.Any(static value => value.FileId < 0 || value.Offset < 0 || value.Length < 0))
        {
            throw new InvalidDataException("Manifest allocations must have unique File IDs and non-negative regions.");
        }

        foreach (NdsManifestAllocation allocation in Allocations)
        {
            ValidateHash(allocation.Sha256, $"Allocations[{allocation.FileId}].Sha256");
        }

        if (Overlays.Select(static value => $"{value.Processor}:{value.OverlayId}").Distinct(StringComparer.Ordinal).Count() != Overlays.Count ||
            Overlays.Any(static value =>
                value.Processor is not NdsProcessor.Arm9 and not NdsProcessor.Arm7 ||
                value.Offset.HasValue != value.Length.HasValue || value.Offset < 0 || value.Length < 0 ||
                value.Offset.HasValue != (value.Sha256 is not null)))
        {
            throw new InvalidDataException("Manifest Overlays must have unique DS processor identities and coherent payload regions.");
        }

        foreach (NdsManifestOverlay overlay in Overlays.Where(static value => value.Sha256 is not null))
        {
            ValidateHash(overlay.Sha256!, $"Overlays[{overlay.Processor}:{overlay.OverlayId}].Sha256");
        }

        if (Banner is not null)
        {
            if (Banner.Offset < 0 || Banner.Length < 0 || Banner.Titles is null)
            {
                throw new InvalidDataException("The manifest Banner contains an invalid region or title map.");
            }

            ValidateHash(Banner.Sha256, "Banner.Sha256");
        }

        if (Dsi is not null &&
            (Dsi.ModcryptArea1 is null || Dsi.ModcryptArea2 is null ||
             Dsi.ModcryptArea1.Offset < 0 || Dsi.ModcryptArea1.Length < 0 ||
             Dsi.ModcryptArea2.Offset < 0 || Dsi.ModcryptArea2.Length < 0))
        {
            throw new InvalidDataException("The manifest DSi modcrypt regions are incomplete or negative.");
        }
    }

    /// <summary>Requires canonical lowercase SHA-256 text so comparison is ordinal and serializer-independent.</summary>
    /// <param name="value">Candidate 64-digit hexadecimal digest.</param>
    /// <param name="name">Manifest path reported in a format error.</param>
    private static void ValidateHash(string? value, string name)
    {
        if (value is null || value.Length != 64 ||
            value.Any(static character => !char.IsAsciiHexDigit(character) || char.IsUpper(character)))
        {
            throw new InvalidDataException($"Manifest field {name} is not a canonical lowercase SHA-256 digest.");
        }
    }

    /// <summary>Builds isolated options so one caller's indentation choice cannot mutate global serializer state.</summary>
    /// <param name="indented">Human-readable whitespace policy.</param>
    /// <returns>Strict web-default JSON options with symbolic enum values.</returns>
    private static JsonSerializerOptions CreateJsonOptions(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = indented,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
