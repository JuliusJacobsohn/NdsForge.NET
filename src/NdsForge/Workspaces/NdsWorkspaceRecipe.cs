using System.Text.Json;
using System.Text.Json.Serialization;

namespace NdsForge;

/// <summary>Describes all native input assets and a complete preservation snapshot using portable workspace-relative paths.</summary>
public sealed class NdsWorkspaceRecipe
{
    /// <summary>Identifies the supported recipe shape independently from the image's binary format version.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Bounds serialized recipe text before JSON parsing or collection materialization.</summary>
    public const int MaximumJsonBytes = 32 * 1024 * 1024;

    /// <summary>Names the recipe file convention used by workspace extraction and command-line imports.</summary>
    public const string FileName = "ndsforge-workspace.json";

    /// <summary>Records the exact supported schema rather than assuming future documents share current semantics.</summary>
    [JsonRequired]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Names a complete original image snapshot that retains gaps, shared regions, and unknown trailing bytes.</summary>
    [JsonRequired]
    public string SourceImagePath { get; init; } = "preservation/source.nds";

    /// <summary>Records original metadata, source identity, File IDs, directories, and overlay relationships independently from host paths.</summary>
    [JsonRequired]
    public NdsImageManifest SourceInventory { get; init; } = new();

    /// <summary>Records every exported native component in deterministic role and numeric File ID order.</summary>
    [JsonRequired]
    public IReadOnlyList<NdsWorkspaceAsset> Assets { get; init; } = [];

    /// <summary>Serializes the validated recipe without embedding payload bytes, external paths, or cryptographic credentials.</summary>
    /// <param name="indented">Adds human-readable whitespace without changing property or asset order.</param>
    /// <returns>A deterministic JSON document using camel-case property and enum names.</returns>
    public string ToJson(bool indented = true)
    {
        Validate();
        string json = JsonSerializer.Serialize(this, CreateOptions(indented));
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumJsonBytes)
        {
            throw new InvalidDataException("Workspace recipe exceeds the JSON byte limit.");
        }
        return json;
    }

    /// <summary>Reads a bounded strict recipe and rejects duplicate properties, unknown fields, unsafe paths, and contradictory assets.</summary>
    /// <param name="json">A complete recipe document, not a host path or executable instruction.</param>
    /// <returns>A validated description whose paths still require host-link checks when opened.</returns>
    public static NdsWorkspaceRecipe ParseJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumJsonBytes)
        {
            throw new InvalidDataException("Workspace recipe exceeds the JSON byte limit.");
        }
        using JsonDocument document = JsonDocument.Parse(json);
        CheckDuplicateProperties(document.RootElement);
        NdsWorkspaceRecipe recipe = document.Deserialize<NdsWorkspaceRecipe>(CreateOptions(indented: false)) ??
            throw new InvalidDataException("A workspace recipe must be a JSON object.");
        recipe.Validate();
        return recipe;
    }

    /// <summary>Enforces complete required roles, bounded regions, unique identities, and collision-free portable input locations.</summary>
    internal void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion || SourceInventory is null || Assets is null || Assets.Count > 1_048_600)
        {
            throw new InvalidDataException("Workspace schema or required collections are invalid.");
        }
        SourceInventory.Validate();
        if (SourceInventory.PhysicalLength is < 0x200 or > 0x100000000L)
        {
            throw new InvalidDataException("Workspace source length must fit the supported image address space.");
        }
        NdsWorkspacePaths.ValidateRelative(SourceImagePath);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { FileName };
        if (!paths.Add(SourceImagePath)) { throw new InvalidDataException("The preservation snapshot cannot replace the recipe document."); }
        var identities = new HashSet<(NdsWorkspaceAssetKind Kind, int? FileId)>();
        foreach (NdsWorkspaceAsset asset in Assets)
        {
            if (asset is null || !Enum.IsDefined(asset.Kind) ||
                (asset.Kind == NdsWorkspaceAssetKind.Allocation) != asset.FileId.HasValue || asset.FileId is < 0 ||
                asset.OriginalOffset < 0 || asset.OriginalLength < 0 ||
                asset.OriginalOffset > SourceInventory.PhysicalLength - asset.OriginalLength ||
                !identities.Add((asset.Kind, asset.FileId)))
            {
                throw new InvalidDataException("Workspace assets require unique roles/File IDs and bounded original regions.");
            }
            NdsWorkspacePaths.ValidateRelative(asset.Path);
            if (!paths.Add(asset.Path)) { throw new InvalidDataException("Workspace asset paths collide on a supported host filesystem."); }
            if (asset.OriginalSha256 is null || asset.OriginalSha256.Length != 64 ||
                asset.OriginalSha256.Any(static character => !char.IsAsciiHexDigit(character) || char.IsUpper(character)))
            {
                throw new InvalidDataException("Workspace asset identity must be lowercase SHA-256 text.");
            }
        }
        foreach (string path in paths)
        {
            int separator = path.IndexOf('/', StringComparison.Ordinal);
            while (separator >= 0)
            {
                if (paths.Contains(path[..separator])) { throw new InvalidDataException("A workspace file path is also used as an asset directory."); }
                separator = path.IndexOf('/', separator + 1);
            }
        }
        foreach (NdsWorkspaceAssetKind required in new[] { NdsWorkspaceAssetKind.Header, NdsWorkspaceAssetKind.Arm9,
            NdsWorkspaceAssetKind.Arm7, NdsWorkspaceAssetKind.FileNameTable, NdsWorkspaceAssetKind.FileAllocationTable })
        {
            if (!identities.Contains((required, null))) { throw new InvalidDataException($"The workspace is missing its required {required} asset."); }
        }
        if (Assets.Count(static asset => asset.Kind == NdsWorkspaceAssetKind.Allocation) != SourceInventory.Allocations.Count ||
            SourceInventory.Allocations.Any(allocation => !identities.Contains((NdsWorkspaceAssetKind.Allocation, allocation.FileId))))
        {
            throw new InvalidDataException("Workspace assets must retain every named and unnamed source allocation.");
        }
    }

    /// <summary>Prevents ambiguous last-property-wins interpretation, including inside nested inventory records.</summary>
    private static void CheckDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) { throw new InvalidDataException("Workspace JSON contains duplicate property names."); }
                CheckDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray()) { CheckDuplicateProperties(item); }
        }
    }

    /// <summary>Uses strict symbolic enums and rejects undocumented fields instead of silently ignoring requested behavior.</summary>
    private static JsonSerializerOptions CreateOptions(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = indented,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
