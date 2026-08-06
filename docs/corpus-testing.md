# Hash-bound ndstool corpus testing

NdsForge's maintainer corpus tests bind each case to the SHA-256 of the complete
image. A developer may place an identical, legally obtained cartridge dump under
any filename or subdirectory beneath `NDSFORGE_CORPUS`; the harness recursively
indexes candidates and activates only exact matches. This makes the public test
contract reproducible without distributing ROMs or trusting descriptive names.

The current corpus contains 57 distinct images after deduplicating 60 inputs from
two private source trees. Three inputs were byte-identical duplicates. It includes
original DS and DSi-enhanced retail layouts, localized releases, modified builds,
large NitroFS trees, ARM9 overlays, SDK footers, and unusual eight-bit filenames.
Six cases are DSi-enhanced.

## Recorded operation matrix

The private generator runs these ndstool 1.50.3 operations for every image:

1. ordinary and verbose information;
2. ordinary, verbose, and wildcard-filtered listing;
3. complete program, table, banner, header, logo, overlay, and NitroFS extraction;
4. binary-input, ELF-input, and bitmap-input image creation;
5. DSi-option probing;
6. header-CRC repair after deliberate corruption;
7. an aligned ARM7 trainer hook;
8. secure-area decrypt, Nintendo-table encrypt, and alternate-table encrypt.

The committed expectations contain 139,289 extraction artifacts. Each artifact
stores only a normalized relative path, byte length, and SHA-256. Operation records
retain exit status and hashes of normalized output streams, not command lines or
raw text. Private full logs and generated outputs stay under the ignored
`fixtures/private` tree.

Normal test runs skip cases whose hash is unavailable. Maintainer and private CI
should set both variables:

```powershell
$env:NDSFORGE_CORPUS = "D:\legal-dumps"
$env:NDSFORGE_REQUIRE_CORPUS = "1"
dotnet test NdsForge.slnx --configuration Release
```

## Compatibility interpretation

Byte equality is required when both tools define the same deterministic output.
All 57 ARM7-hook images are byte-equal. All 139,289 common extraction artifacts
match after modeling historical host-filename conversion. NdsForge structural
rebuild tests additionally compare every program, named file, unnamed allocation,
overlay payload, directory, and banner by semantic identity and hash.

Some ndstool behavior must not become NdsForge's default:

- ndstool 1.50.3 exports only the 512-byte common header and 2,112-byte legacy
  banner prefix from DSi-enhanced images. NdsForge exposes the complete 4 KiB
  extended header and 9,152-byte animated banner while matching the common prefix.
- ndstool's overlay extraction indexes the FAT with the runtime overlay ID instead
  of the overlay record's file ID. NdsForge keeps these independent and resolves
  payloads through `NdsOverlay.FileId`.
- ndstool's CRC-repair path writes the header-declared size from a smaller in-memory
  header object. On this corpus it changes bytes even when the original CRC is
  already valid. NdsForge's preservation editor leaves all 57 valid headers
  byte-identical.
- ndstool's create-from-directory workflow derives FNT ordering from host directory
  enumeration. NdsForge recipes use deterministic ordinal ordering and test semantic
  payload/overlay preservation rather than treating host-dependent file IDs as an
  oracle. Used-image length is compared for original DS images; the 1.50.3 profile
  deliberately rejects DSi creation because that historical tool predates it.
- the tested 1.50.3 executable rejects newer DSi switches that are present in the
  current source tree. The probe exit is retained so a future oracle upgrade is an
  explicit reviewed change.

These conclusions are cross-checked against devkitPro's upstream tracker. The
[overlay misnumbering report](https://github.com/devkitPro/ndstool/issues/16)
describes the same overlay-ID/file-ID defect reproduced by this corpus. The
[DSi hash-tree request](https://github.com/devkitPro/ndstool/issues/13) documents
functionality NdsForge already exposes, while the historical
[DSi banner reservation bug](https://github.com/devkitPro/ndstool/issues/3) and
[ELF alignment report](https://github.com/devkitPro/ndstool/issues/12) inform the
corresponding compatibility tests without defining modern default behavior.

## Maintainer refresh

The ignored private library and oracle are managed by `tools/NdsForge.Corpus`:

```powershell
dotnet run --project tools/NdsForge.Corpus -- merge <additional-root> <library> <catalog.json>
dotnet run --project tools/NdsForge.Corpus -- refresh <library> <catalog.json>
dotnet run --project tools/NdsForge.Corpus -- oracle <library> <private-oracle> <ndstool.exe>
dotnet run --project tools/NdsForge.Corpus -- expectations <library> <private-oracle> `
  tests/NdsForge.CompatibilityTests/Corpus/Expectations <catalog.json>
```

`merge` recursively hashes additions and skips byte-identical images. `refresh`
recomputes canonical titles/locales and renames only within the private library.
`expectations` removes source paths, arguments, timings, raw logs, and payloads.
Reviewers should still run a leak scan and verify all C# files remain below the
repository's 500-line limit before committing refreshed data.
