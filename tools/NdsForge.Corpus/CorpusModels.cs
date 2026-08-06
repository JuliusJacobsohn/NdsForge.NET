using System.Text.Json.Serialization;

namespace NdsForge.Corpus;

/// <summary>Records stable private-corpus identity without embedding any cartridge payload bytes.</summary>
internal sealed record CorpusCatalog(
    int SchemaVersion,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<CorpusRom> Roms);

/// <summary>Connects an imported filename to its content-derived canonical name and cartridge identity.</summary>
internal sealed record CorpusRom(
    string SourceName,
    string FileName,
    long Length,
    string Sha256,
    string HeaderTitle,
    string GameCode,
    string MakerCode,
    NdsImageKind Kind,
    byte Revision,
    string Region,
    string PreferredLanguage,
    string DisplayTitle,
    IReadOnlyDictionary<string, string> BannerTitles);

/// <summary>Captures every ndstool operation and derived artifact digest for one private image.</summary>
internal sealed record CorpusOracle(
    int SchemaVersion,
    DateTimeOffset GeneratedAt,
    string NdstoolSha256,
    CorpusRom Rom,
    IReadOnlyList<OracleOperation> Operations);

/// <summary>Preserves command outcome, console evidence, and resulting file tree without retaining duplicated ROM data.</summary>
internal sealed record OracleOperation(
    string Name,
    IReadOnlyList<string> Arguments,
    int ExitCode,
    long DurationMilliseconds,
    string StandardOutput,
    string StandardError,
    IReadOnlyList<OracleArtifact> Artifacts);

/// <summary>Addresses a generated file by relative path, exact length, and SHA-256 rather than proprietary contents.</summary>
internal sealed record OracleArtifact(string Path, long Length, string Sha256);

/// <summary>Indexes public expectation documents without exposing any original private source paths.</summary>
internal sealed record CorpusExpectationIndex(
    int SchemaVersion,
    string NdstoolSha256,
    IReadOnlyList<CorpusExpectationIndexEntry> Cases);

/// <summary>Lets the test harness discover a case by stable display name, content identity, and expectation filename.</summary>
internal sealed record CorpusExpectationIndexEntry(string Name, string RomSha256, string ExpectationFile);

/// <summary>Binds normalized ndstool evidence and NdsForge diagnostics to one exact complete-image hash.</summary>
internal sealed record CorpusExpectation(
    int SchemaVersion,
    string NdstoolSha256,
    ExpectedRom Rom,
    IReadOnlyList<ExpectedDiagnostic> ValidationDiagnostics,
    IReadOnlyList<ExpectedOperation> Operations);

/// <summary>Publishes non-sensitive cartridge identity and localized titles while omitting import filenames.</summary>
internal sealed record ExpectedRom(
    string FileName,
    long Length,
    string Sha256,
    string HeaderTitle,
    string GameCode,
    string MakerCode,
    NdsImageKind Kind,
    byte Revision,
    string Region,
    string PreferredLanguage,
    string DisplayTitle,
    IReadOnlyDictionary<string, string> BannerTitles);

/// <summary>Freezes one keyless validation finding so parser or validator changes remain reviewable.</summary>
internal sealed record ExpectedDiagnostic(
    string Code,
    NdsDiagnosticSeverity Severity,
    long? Offset,
    long? Length);

/// <summary>Publishes process status, normalized console hashes, and artifact hashes without raw logs or payloads.</summary>
internal sealed record ExpectedOperation(
    string Name,
    int ExitCode,
    string StandardOutputSha256,
    string StandardErrorSha256,
    IReadOnlyList<OracleArtifact> Artifacts);

/// <summary>Serializes enum names and camel-case fields into reviewable, platform-independent oracle documents.</summary>
[JsonSerializable(typeof(CorpusCatalog))]
[JsonSerializable(typeof(CorpusOracle))]
[JsonSerializable(typeof(CorpusExpectationIndex))]
[JsonSerializable(typeof(CorpusExpectation))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
internal sealed partial class CorpusJsonContext : JsonSerializerContext;
