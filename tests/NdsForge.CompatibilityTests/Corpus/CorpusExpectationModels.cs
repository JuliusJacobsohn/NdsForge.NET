namespace NdsForge.CompatibilityTests.Corpus;

#pragma warning disable CA1812 // System.Text.Json constructs these DTOs through reflection.

/// <summary>Describes the committed corpus cases without assuming any particular local ROM filename.</summary>
internal sealed record CorpusExpectationIndex(
    int SchemaVersion,
    string NdstoolSha256,
    IReadOnlyList<CorpusExpectationIndexEntry> Cases);

/// <summary>Connects a human-readable case label and exact full-image hash to its public oracle document.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515", Justification = "xUnit theory arguments must be publicly visible.")]
public sealed record CorpusExpectationIndexEntry(
    string Name,
    string RomSha256,
    string ExpectationFile);

/// <summary>Freezes payload-free observations for one exact cartridge dump and one ndstool executable.</summary>
internal sealed record CorpusExpectation(
    int SchemaVersion,
    string NdstoolSha256,
    ExpectedRom Rom,
    IReadOnlyList<ExpectedDiagnostic> ValidationDiagnostics,
    IReadOnlyList<ExpectedOperation> Operations);

/// <summary>Captures public cartridge metadata whose value can be independently reproduced by parsing the supplied dump.</summary>
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

/// <summary>Represents a keyless NdsForge finding without retaining unstable explanatory prose.</summary>
internal sealed record ExpectedDiagnostic(
    string Code,
    NdsDiagnosticSeverity Severity,
    long? Offset,
    long? Length);

/// <summary>Records an ndstool command's status and content-addressed outputs while excluding command lines and console text.</summary>
internal sealed record ExpectedOperation(
    string Name,
    int ExitCode,
    string StandardOutputSha256,
    string StandardErrorSha256,
    IReadOnlyList<ExpectedArtifact> Artifacts);

/// <summary>Identifies a generated payload solely by normalized relative path, byte length, and SHA-256.</summary>
internal sealed record ExpectedArtifact(string Path, long Length, string Sha256);

#pragma warning restore CA1812
