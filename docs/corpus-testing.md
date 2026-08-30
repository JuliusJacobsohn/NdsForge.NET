# Hash-bound ndstool corpus testing

NdsForge's maintainer corpus tests bind each case to the SHA-256 of the complete
image. A developer may place an identical, legally obtained cartridge dump under
any filename or subdirectory beneath `NDSFORGE_CORPUS`; the harness recursively
indexes candidates and activates only exact matches. This makes the public test
contract reproducible without distributing ROMs or trusting descriptive names.

The current corpus contains 142 distinct images after deduplicating 160 inputs from
three private source trees. Eighteen inputs were byte-identical duplicates. It includes
original DS and DSi-enhanced retail layouts, localized releases, modified builds,
large NitroFS trees, ARM9 overlays, SDK footers, and unusual eight-bit filenames.
Nine cases are DSi-enhanced.

## Recorded operation matrix

The private generator records these ndstool 1.50.3 operations for every image:

1. complete program, table, banner, header, logo, Overlay, and NitroFS extraction;
2. binary-input image creation from the extracted components when extraction succeeds;
3. header-CRC repair after deliberate corruption;
4. an aligned ARM7 trainer hook.

Every recorded operation is consumed by a NdsForge byte-equality or semantic
differential assertion. Commands that merely run ndstool without testing NdsForge
are deliberately excluded from both the generator and compatibility totals.

The committed expectations contain 393,581 extraction artifacts. Each artifact
stores only a normalized relative path, byte length, and SHA-256. Operation records
retain exit status and hashes of normalized output streams, not command lines or
raw text. Private full logs and generated outputs stay under the ignored
`fixtures/private` tree.

An enforced feature inventory currently records 133 DS images, nine DSi-enhanced
images, nine animated banners, 120 images with ARM9 Overlays, 1,716 Overlay-ID/File-ID
mismatches, 9,074 unnamed FAT allocations, 133 ARM9 SDK footers, and 3,208 high-byte
FNT names. One image has a valid raw filename namespace that the pinned Windows
reference executable cannot materialize through its host code page; it remains
active for semantic rebuild testing and comparison of the extraction artifacts
that were produced. Only complete extraction-set equality and reference rebuild
size are unavailable for this image. The inventory also deliberately
records zero real ARM7 Overlay tables. That last value is a known fixture gap, not
claimed coverage; an exact legal fixture should be added when available.

Normal test runs skip cases whose hash is unavailable. Maintainer and private CI
should set both variables:

```powershell
$env:NDSFORGE_CORPUS = "D:\legal-dumps"
$env:NDSFORGE_REQUIRE_CORPUS = "1"
dotnet test NdsForge.slnx --configuration Release
```

## Compatibility interpretation

Byte equality is required when both tools define the same deterministic output.
All 142 ARM7-hook images are byte-equal. The 393,549 artifacts from 141 complete
reference extractions and the 32 artifacts from one partial extraction are
compared after modeling historical host-filename conversion. NdsForge structural
rebuild tests additionally compare every program, named file, unnamed allocation,
Overlay payload and metadata, directory, banner, common identity, and DSi policy
field by semantic identity and hash for 141 rebuildable images. One additional
image declares authenticated overlays without a table pointer; its test requires
an explicit failure before an output image is published. Rebuilt validation may retain a known source
error but may not introduce a new error category. Imported modcrypt intervals
anchored to a relocated Program are remapped to that Program's final offset.

Fourteen images also contain a fixed Download Play signature trailer at the
declared used-image boundary. Dedicated tests freeze its bytes against each input
identity and require exact no-op copies plus preservation through rebuilds and
header edits. Structural output-size comparison accounts separately for these
136 retained post-used bytes; they are not part of the meaningful used extent.

Some ndstool behavior must not become NdsForge's default:

- ndstool 1.50.3 exports only the 512-byte common header and 2,112-byte legacy
  banner prefix from DSi-enhanced images. NdsForge exposes the complete 4 KiB
  extended header and 9,152-byte animated banner while matching the common prefix.
- ndstool's overlay extraction indexes the FAT with the runtime overlay ID instead
  of the overlay record's file ID. NdsForge keeps these independent and resolves
  payloads through `NdsOverlay.FileId`.
- ndstool's CRC-repair path writes the header-declared size from a smaller in-memory
  header object. On this corpus it changes bytes even when the original CRC is
  already valid. The differential test corrupts the same stored CRC byte used by
  the oracle and requires NdsForge to restore the exact original full-image SHA-256.
- ndstool's create-from-directory workflow derives FNT ordering from host directory
  enumeration. NdsForge recipes use deterministic ordinal ordering and test semantic
  payload/overlay preservation rather than treating host-dependent file IDs as an
  oracle. Used-image length is compared for original DS images; the 1.50.3 profile
  deliberately rejects DSi creation because that historical tool predates it.
- When the final file is empty, ndstool can write its aligned FAT offset beyond
  physical EOF while declaring a larger used-image size. NdsForge includes the
  exact missing padding instead, keeping every FAT entry within the output.
  This corpus contains one such case; the test checks the last nonempty payload
  end, final empty-file offset, physical length, and used-size relationship.

These conclusions are cross-checked against devkitPro's upstream tracker. The
[overlay misnumbering report](https://github.com/devkitPro/ndstool/issues/16)
describes the same overlay-ID/file-ID defect reproduced by this corpus. The
[DSi hash-tree request](https://github.com/devkitPro/ndstool/issues/13) documents
functionality NdsForge already exposes, while the historical
[DSi banner reservation bug](https://github.com/devkitPro/ndstool/issues/3) and
[ELF alignment report](https://github.com/devkitPro/ndstool/issues/12) inform the
corresponding compatibility tests without defining modern default behavior.

## Digital-SRL fixtures

The separate private digital-SRL matrix contains five exact content identities and twenty program payloads. Set `NDSFORGE_DIGITAL_CORPUS` to a directory containing them, or place them in ignored `fixtures/private/digital-srl`. The test accepts `.nds`, `.dsi`, `.srl`, and `.app` names and verifies complete SHA-256 identities; filenames carry no format meaning. `NDSFORGE_REQUIRE_CORPUS=1` makes an incomplete digital matrix fail rather than skip. Tests lock metadata and payload digests, byte-exact no-op copies, patterned post-header preservation, retained capacity bytes 0 and 10, and semantic rebuild behavior. Pre-existing authenticity errors are retained and require explicit verification opt-out for a raw no-op copy.

The 142-game cartridge matrix separately checks all reserved post-header regions, including the two nonzero regions, and verifies their preservation during structural transformations. Neither digital binaries nor cartridge payloads are committed.

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
