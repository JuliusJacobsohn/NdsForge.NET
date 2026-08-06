# NdsForge.NET

NdsForge.NET is a production-oriented, pure C# library for reading, validating,
extracting, editing, comparing, and building Nintendo DS and DSi software images.
It targets .NET 10, has no runtime dependencies, and keeps the command line as a
thin client over the same public object model available to applications.

The 1.0 API is designed around developer workflows rather than command switches:
immutable parsed images, explicit edit sessions, deterministic build recipes,
stream-first I/O, structured validation findings, safe extraction, and stable
JSON manifests. A named `Ndstool1503` build profile and executable-oracle tests
cover byte-exact interoperability where ndstool provides a deterministic result.

NdsForge.NET is independent and is not affiliated with Nintendo or devkitPro. It
contains no ROM images, firmware, keys, certificates, or proprietary logo data.

## Install

```powershell
dotnet add package NdsForge --version 1.0.0
dotnet tool install --global NdsForge.Cli --version 1.0.0
```

Both packages require the .NET 10 runtime. The library supports paths, seekable
streams, and in-memory data; large image payloads can be read without first
materializing the complete image.

## Read, query, and validate

```csharp
using NdsForge;

await using NdsImage image = await NdsImage.OpenAsync("game.nds");

Console.WriteLine($"{image.Header.Title} [{image.Header.GameCode}]");
foreach (NdsFile file in image.FileSystem.Files)
{
    Console.WriteLine($"{file.Id:D5} {file.Data.Length,10} {file.FullPath}");
}

NdsValidationResult validation = image.Validate();
foreach (NdsDiagnostic finding in validation.Diagnostics)
{
    Console.WriteLine($"{finding.Severity}: {finding.Code}: {finding.Message}");
}
```

Parsing establishes a safe navigable object graph; validation is separate and
never mutates the input. `NdsReadOptions` caps hostile table sizes, directory
counts/depth, overlay counts, and banner allocation before they consume memory.
Validation can additionally receive caller-owned KEY1, DSi HMAC, and RSA trust
inputs. Integrity and authenticity are deliberately reported as different facts.

## Extract safely

```csharp
await image.ExtractAsync(
    "workspace",
    new NdsExtractionOptions
    {
        Components = NdsImageComponent.Programs |
            NdsImageComponent.NitroFileSystem |
            NdsImageComponent.Banner,
        OverwritePolicy = NdsOverwritePolicy.Fail,
        FileFilter = file => file.FullPath.StartsWith("/data/", StringComparison.Ordinal),
    });
```

Host extraction rejects path traversal, invalid/reserved names, collisions, and
reparse-point redirection. `Fail`, `Skip`, and atomic `Overwrite` policies make
reruns explicit. Individual `NdsFile` and `NdsRegion` payloads remain available
as streams when no host directory is wanted.

## Make preservation-oriented edits

```csharp
using NdsImage source = NdsImage.Open("game.nds");
NdsImageEditor edit = source.Edit();

edit.Header.Title = "MY MOD";
edit.ReplaceFile("/data/config.bin", File.ReadAllBytes("config.bin"));
edit.RepairHeaderCrc().RepairBannerCrcs();

NdsEditPlan review = edit.Plan;
NdsSaveResult saved = await edit.SaveAsync(
    "mod.nds",
    new NdsWriteOptions { VerifyOutput = true });
```

An unchanged edit is byte-identical. Same-size and smaller replacements remain in
their allocations; larger replacements relocate under an explicit alignment
policy. Saving to a path uses a verified temporary sibling and an atomic move, so
failure does not truncate an existing destination. Structural NitroFS changes use
`NdsImageBuilder.FromImageAsync` instead of pretending they are local byte edits.

## Build deterministic DS images

```csharp
var builder = new NdsImageBuilder
{
    Title = "HOMEBREW",
    GameCode = "HB01",
    MakerCode = "HB",
    Arm9 = new NdsProgramDefinition(
        NdsProcessor.Arm9, File.ReadAllBytes("arm9.bin"), 0x02000000, 0x02000000),
    Arm7 = new NdsProgramDefinition(
        NdsProcessor.Arm7, File.ReadAllBytes("arm7.bin"), 0x02380000, 0x02380000),
};

await builder.FileSystem.ImportDirectoryAsync("data");
builder.Banner = new NdsBannerBuilder()
    .SetTitle(NdsBannerLanguage.English, "Homebrew example")
    .Build();

NdsImageBuildResult result = await builder.WriteAsync("homebrew.nds");
```

Build recipes own copies of caller byte buffers. Repeated builds from identical
state produce identical component order, offsets, padding, and checksums. Raw
ARM programs and validated little-endian ARM ELF32 imports are supported, as are
ARM9/ARM7 overlays, empty directories, banners, caller-supplied encoded logos,
SDK footers, DSi extended programs, digest tables, HMAC fields, and RSA signing.

Use `new NdsImageBuildOptions { Profile = NdsImageBuildProfile.Ndstool1503 }`
when exact ndstool 1.50.3 layout is an interoperability requirement. The ordinary
deterministic profile uses safer modern defaults and does not inherit historical
layout quirks.

## DSi, secure-area, and authentication APIs

The package exposes narrowly typed cryptographic operations without containing
secret material:

- inspect, encrypt, decrypt, and validate the 16 KiB KEY1 secure area using a
  caller-supplied `NdsKey1KeyTable`;
- derive retail or developer modcrypt contexts and transform either declared
  area through streaming AES-CTR operations;
- build and verify the DSi sector/block digest hierarchy and HMAC fields;
- verify DSi RSA signatures with a caller-supplied public key or sign a build
  with a caller-supplied private key;
- apply the isolated `NdsLegacyArm7Hook` compatibility transform, with byte-level
  behavior checked against ndstool 1.50.3.

Cryptographic keys are copied into private buffers, are never logged, and are not
included in manifests. Hash validity means bytes are internally consistent; it
does not by itself establish that Nintendo or another trusted party signed them.

## Manifests and semantic diffs

```csharp
NdsImageManifest manifest = await image.CreateManifestAsync();
await File.WriteAllTextAsync("game.manifest.json", manifest.ToJson());

using NdsImage other = NdsImage.Open("rebuilt.nds");
NdsImageDiff diff = await image.CompareAsync(other);
if (!diff.AreEquivalent)
{
    foreach (NdsSemanticDifference change in diff.Differences)
    {
        Console.WriteLine($"{change.Kind}: {change.Path}");
    }
}
```

The versioned manifest records image/component SHA-256 values, identity fields,
regions, NitroFS paths and file IDs, overlays, banners, and relevant DSi metadata.
Semantic comparison complements a raw byte diff by explaining which component,
identity, association, or layout changed.

## Command line

```text
ndsforge inspect <image.nds>
ndsforge validate <image.nds>
ndsforge list <image.nds> [--long]
ndsforge extract <image.nds> <directory> [--overwrite]
ndsforge replace <image.nds> <nitro-path> <file> <output.nds> [--overwrite]
ndsforge manifest <image.nds> [output.json]
ndsforge diff <left.nds> <right.nds>
```

Exit code `0` indicates success/equivalence, `1` a validation or comparison
failure, `2` invalid command usage, and `130` cancellation.

## Compatibility and quality gates

The test suite combines synthetic unit/integration fixtures with opt-in local
oracles. It covers malformed/truncated images, table limits, path attacks,
deterministic rebuilds, edits and repairs, every supported banner family, DSi
digests/HMAC/RSA/modcrypt, KEY1 known behavior, ELF and overlays, manifests, and
whole command workflows. Where comparison is meaningful, builds, extraction,
secure-area transforms, and the legacy hook are checked against ndstool 1.50.3.

Every library declaration is checked for meaningful XML documentation during the
test run. The build treats current .NET analyzers as errors, emits deterministic
assemblies and XML documentation, and limits each C# source file to 500 lines.

To run the public tests:

```powershell
dotnet restore
dotnet test NdsForge.slnx --configuration Release
dotnet pack NdsForge.slnx --configuration Release --output artifacts/packages
```

Optional private compatibility tests use environment variables and never copy
fixtures into the repository:

```powershell
$env:NDSFORGE_NDSTOOL = "C:\tools\ndstool.exe"
$env:NDSFORGE_TEST_ROM = "C:\private\game.nds"
$env:NDSFORGE_NDSTOOL_SOURCE = "C:\src\ndstool\source\encryption.cpp"
dotnet test NdsForge.slnx --configuration Release
```

The large differential corpus is content-addressed rather than filename-addressed.
Set `NDSFORGE_CORPUS` to any directory tree containing legally dumped images; a
case runs only when a complete-file SHA-256 matches its committed expectation.
Set `NDSFORGE_REQUIRE_CORPUS=1` in maintainer CI to turn any missing case into a
failure. The repository contains only metadata, process outcomes, lengths, and
SHA-256 values—not ROMs, extracted payloads, original paths, or console logs.

See [the corpus testing design](docs/corpus-testing.md) for the feature matrix,
known ndstool divergences, and the procedure for refreshing expectations.

## Scope

NdsForge is an image/container library, not an emulator, disassembler, save-game
editor, or game-specific archive toolkit. Core banner APIs use native indexed
pixels and RGBA32 buffers; PNG/GIF authoring belongs in an adapter so the binary
library remains dependency-free. Compression is preserved and described but is
not guessed for game-specific formats.

## License

NdsForge.NET is licensed under the [MIT License](LICENSE). The implementation is
written from public format documentation and independently designed APIs, with
existing tools used as behavioral oracles. No third-party implementation source
is copied, translated, linked, or distributed in the packages.
