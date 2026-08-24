<p align="center">
  <img src="https://raw.githubusercontent.com/JuliusJacobsohn/NdsForge.NET/main/assets/branding/ndsforge-wordmark.svg" alt="NdsForge: a dual-screen mark joined by a forge spark" width="720">
</p>

# NdsForge.NET

> [!WARNING]
> Created with substantial help from **GPT-5.6-Sol Ultra** and tested against my own use cases. Please do not treat this project as a measure of my abilities as a developer, for better or worse.

[![CI](https://github.com/JuliusJacobsohn/NdsForge.NET/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/JuliusJacobsohn/NdsForge.NET/actions/workflows/ci.yml)
[![Documentation](https://github.com/JuliusJacobsohn/NdsForge.NET/actions/workflows/docs.yml/badge.svg?branch=main)](https://juliusjacobsohn.github.io/NdsForge.NET/)
[![NuGet](https://img.shields.io/nuget/v/NdsForge.svg)](https://www.nuget.org/packages/NdsForge)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**[Get NdsForge on NuGet](https://www.nuget.org/packages/NdsForge)** · **[Browse the API documentation →](https://juliusjacobsohn.github.io/NdsForge.NET/)**

[Getting started](docs/getting-started.md) · [Formats and safety](docs/formats-and-safety.md) · [Nitro codecs](docs/nitro-codecs.md) · [CLI reference](docs/cli.md) · [Corpus testing](docs/corpus-testing.md)

NdsForge is a pure C# library for inspecting, validating, extracting, editing, comparing, and building Nintendo DS and DSi software images. Its object-oriented API supports path, stream, and in-memory workflows without shelling out to native tools.

## Quick start

Install the library:

```shell
dotnet add package NdsForge
```

Install `NdsForge.Nitro` separately when an application needs reusable Nitro compression or container codecs without the ROM-image model:

```shell
dotnet add package NdsForge.Nitro
```

```csharp
using NdsForge;

await using NdsImage image = await NdsImage.OpenAsync("game.nds");

Console.WriteLine($"{image.Header.Title} [{image.Header.GameCode}]");
Console.WriteLine($"{image.FileSystem.Files.Count} NitroFS files");

NdsValidationResult validation = image.Validate();
foreach (NdsDiagnostic diagnostic in validation.Diagnostics)
{
    Console.WriteLine($"{diagnostic.Severity}: {diagnostic.Code}: {diagnostic.Message}");
}
```

Parsing creates a navigable image model without modifying the source. Validation is explicit and reports structured findings instead of printing tool-specific text.

## Highlights

- Typed DS, DSi-enhanced, and DSi-exclusive headers
- NitroFS directories, files, IDs, allocations, overlays, programs, banners, and SDK footers
- Bounded parsing and structured integrity diagnostics for untrusted images
- Safe selective extraction with traversal, collision, and reparse-point defenses
- Preservation-oriented edits with reviewable plans and atomic path writes
- Deterministic DS and DSi image construction from binary or supported ELF inputs
- Stable JSON manifests and semantic image comparison
- Caller-supplied KEY1, DSi digest, HMAC, modcrypt, and RSA operations
- An optional `Ndstool1503` build profile for verified interoperability cases
- A dependency-free `NdsForge.Nitro` companion package for BLZ and other reusable Nitro formats
- A cross-platform `ndsforge` .NET command-line tool over the same library

## Edit an image

```csharp
using NdsImage source = NdsImage.Open("game.nds");
NdsImageEditor edit = source.Edit();

edit.Header.Title = "MY MOD";
edit.ReplaceFile("/data/config.bin", File.ReadAllBytes("config.bin"));
edit.RepairHeaderCrc().RepairBannerCrcs();

NdsSaveResult result = await edit.SaveAsync(
    "mod.nds",
    new NdsWriteOptions { VerifyOutput = true });
```

An unchanged preservation edit is byte-identical. Structural file-system changes use `NdsImageBuilder.FromImageAsync`, keeping local patching and full rebuilding as separate, explicit workflows.

## Command line

```shell
dotnet tool install --global NdsForge.Cli

ndsforge inspect game.nds
ndsforge validate game.nds
ndsforge list game.nds --long
ndsforge extract game.nds workspace
ndsforge manifest game.nds game.manifest.json
ndsforge diff game.nds rebuilt.nds
```

See the [CLI reference](docs/cli.md) for commands, options, and exit codes.

## Compatibility and scope

NdsForge targets .NET 10 and has no runtime NuGet dependencies. It handles Nintendo DS image/container structures; it is not an emulator, disassembler, save editor, or game-specific archive library. PNG/GIF authoring and proprietary game compression belong in separate adapters.

No ROMs, firmware, keys, certificates, or proprietary logo data are included. Cryptographic operations require caller-owned key material. Some authenticity checks cannot be completed from public image data alone, and validation distinguishes missing trust inputs from invalid content.

The compatibility suite uses synthetic fixtures and hash-bound expectations for legally dumped private images. Where an ndstool comparison is meaningful and deterministic, NdsForge records byte-level or semantic parity. Read [formats, compatibility, and safety](docs/formats-and-safety.md) before handling unknown images or performing authenticated DSi builds.

Contributions should follow [CONTRIBUTING.md](CONTRIBUTING.md), security reports follow [SECURITY.md](SECURITY.md), and releases follow [RELEASING.md](RELEASING.md). Licensed under the [MIT License](LICENSE).

NdsForge.NET is maintained by Julius Jacobsohn. It is not affiliated with or endorsed by Nintendo, devkitPro, or the maintainers of ndstool.
