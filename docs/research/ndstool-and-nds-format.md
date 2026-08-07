# NdsForge.NET: ndstool capability and Nintendo DS ROM format research

Research snapshot: 2026-08-06. The upstream code snapshot used throughout is
`devkitPro/ndstool` commit `76e8b681bb225d945a48852821e03114e6c7ce1c`
(release 2.3.1, 2024-01-08). This report is a product and implementation brief,
not legal advice.

## Executive conclusions

1. The original ndstool is much more than a NitroFS extractor. It inspects and
   repairs headers, lists and extracts NitroFS, extracts individual ROM regions,
   builds DS and DSi homebrew images from binaries or ELF32 files, handles
   overlays and banners, transforms the secure area, and contains a legacy ARM7
   hook operation. Its own CLI describes most of this surface, while recent DSi
   and ELF-overlay behavior is visible only in source. See the
   [CLI help table](https://github.com/devkitPro/ndstool/blob/76e8b681bb225d945a48852821e03114e6c7ce1c/source/ndstool.cpp#L94-L135),
   [action parser](https://github.com/devkitPro/ndstool/blob/76e8b681bb225d945a48852821e03114e6c7ce1c/source/ndstool.cpp#L191-L409),
   and [image creation path](https://github.com/devkitPro/ndstool/blob/76e8b681bb225d945a48852821e03114e6c7ce1c/source/ndscreate.cpp#L310-L816).
   A current BlocksDS fork adds multilingual and animated banners, modern image
   inputs, multiple NitroFS roots, and correctness fixes. Neither fork is a strict
   superset, so "full" should mean their safe union, with legacy crypto/hook
   behavior isolated.
2. A useful .NET library should expose a ROM document model and an explicit edit
   session, not wrap CLI switches. It should support streams as well as paths,
   preserve unknown bytes by default, offer deterministic rebuilds, and report
   validation problems as data rather than printing or terminating the process.
3. Treat ROMs as hostile input. Every offset, size, count, file ID, directory ID,
   tree edge, and extraction path requires checked validation before allocation or
   I/O. The old implementation trusts many values and calls `exit(1)` from deep
   helpers; that behavior is not suitable for a reusable package.
4. Upstream ships the full GNU GPL version 3 text in
   [`COPYING`](https://github.com/devkitPro/ndstool/blob/76e8b681bb225d945a48852821e03114e6c7ce1c/COPYING).
   A translated or source-adapted port has significant derivative-work licensing
   implications. Because this project was designed after inspecting upstream
   source, it should not be represented as a clean-room implementation. If a
   permissive package license is desired, obtain qualified legal review and keep
   the implementation demonstrably specification-driven; otherwise use a
   GPLv3-compatible distribution strategy. The repository does not state
   "or later", so do not silently label the upstream code `GPL-3.0-or-later`.
5. Do not ship commercial ROMs, extracted game data, keys/BIOS material, or a
   Nintendo logo asset in the package or tests. Nintendo states that its product
   IP includes software, artwork, names, symbols, and logos; callers can supply
   any assets they are entitled to use. See Nintendo's
   [Intellectual Property & Piracy FAQ](https://en-americas-support.nintendo.com/app/answers/detail/a_id/55888/~/intellectual-property-%26-piracy-faq).

## Upstream provenance and source architecture

The current upstream release declares version 2.3.1 in
[`configure.ac`](https://github.com/devkitPro/ndstool/blob/76e8b681bb225d945a48852821e03114e6c7ce1c/configure.ac)
and identifies Rafael Vuijk, Dave Murphy, and Alexei Karpenko in the executable's
[title output](https://github.com/devkitPro/ndstool/blob/76e8b681bb225d945a48852821e03114e6c7ce1c/source/ndstool.cpp#L66-L70).
The repository is a C/C++ Autotools application with no test project in its tree.
Its major modules are:

| Module | Responsibility in upstream |
|---|---|
| `ndstool.cpp` | Global CLI state, help, option parsing, ordered action dispatch |
| `header.*` | Packed base/extended header model, info display, CRC/SHA checks, optional release-list lookup |
| `ndsextract.*` | Generic region extraction, FNT/FAT traversal, wildcard matching, overlay extraction |
| `ndscreate.*`, `ndstree.*` | Directory scan, layout, FNT/FAT generation, binaries/ELF insertion, DS/DSi image finalization |
| `elf.*` | ELF32 program-header ingestion and newer ELF overlay support |
| `banner.*`, `raster.*`, `logo.*` | Banner CRC/title/icon creation, BMP/GRF import, cartridge-logo conversion |
| `encryption.*`, `bigint.*` | DS KEY1 secure-area transformation and related tables/test patterns |
| `hook.*` | Legacy CRC-preserving ARM7 append/header patch operation |
| `crc.*`, `sha1.*` | Hash/checksum primitives |
| `ndscodes.cpp` | Country and maker display-name tables |

The architecture is procedural and process-oriented: writable globals are
declared in [`ndstool.h`](https://github.com/devkitPro/ndstool/blob/76e8b681bb225d945a48852821e03114e6c7ce1c/source/ndstool.h),
and helpers share `FILE*`, header, option, and counter state. Packed structs are
read directly from disk. The .NET design should preserve binary behavior, not
this architecture.

Recent upstream history materially expands the original feature set. The 2.2.x
changes added extended DSi header/banner handling and DSi image fixes; 2.3.x added
ELF overlay sections, overlay BSS sizes, and correct virtual-to-physical ELF
entrypoint conversion. The implementation is visible in
[`elf.cpp`](https://github.com/devkitPro/ndstool/blob/76e8b681bb225d945a48852821e03114e6c7ce1c/source/elf.cpp)
and the repository's
[`v2.3.1` history](https://github.com/devkitPro/ndstool/commits/v2.3.1/).

### Active BlocksDS fork and the meaning of "full"

BlocksDS maintains a divergent ndstool fork; this report uses commit
`4d8ef3e7451dd0c633837318612e13dbe932b52f` (2026-03-18). Its current
[help table](https://github.com/blocksds/ndstool/blob/4d8ef3e7451dd0c633837318612e13dbe932b52f/source/ndstool.cpp#L108-L154)
adds separate header/banner repair (`-fh`, `-fb`), `-V`, explicit ARM9i/ARM7i
help, multiple NitroFS input roots, BMP/GIF/PNG images, static and animated icon
inputs, per-language banner text, two latency profiles, explicit DSi unit code,
and PassMe on/off controls. The BlocksDS first-party
[build-process documentation](https://blocksds.skylyrac.net/docs/internal/build_process/)
describes its ELF, NitroFS, DS, and DSi flow, while its
[changelog](https://blocksds.skylyrac.net/docs/changelog/) records multilingual
titles, icons, multiple roots, and header/banner/secure-area CRC and latency fixes.

BlocksDS deliberately removed secure-area key/encryption and the ARM7 trainer
hook as unnecessary for its homebrew focus.
[Removal commit](https://github.com/blocksds/ndstool/commit/96d0f3ee7d183804ae5d4a3f89d3d0a1699c5ad3)
The compatibility targets should therefore be explicit:

1. devkitPro 2.3.1 inspect/list/extract/create/repair parity;
2. BlocksDS modern banner, image, multiple-root, DSi, and correctness behavior;
3. legacy commercial-style secure-area and trainer-hook behavior only as a
   consciously licensed, isolated, opt-in component.

## Binary format facts that drive the model

### Cartridge header and regions

The base header maps identity, ARM9/ARM7 ROM/load/entry ranges, FNT/FAT ranges,
ARM9/ARM7 overlay tables, banner offset, secure-area CRC, used-ROM length, logo,
and CRCs. GBATEK gives the field layout from `0x000` through the base header and
states that the logo CRC covers `0x0C0..0x15B`, while the header CRC covers
`0x000..0x15D`.
[GBATEK cartridge header](https://www.akkit.org/info/gbatek.htm#dscartridgeheader)

The reusable model must therefore distinguish:

- physical ROM ranges (`offset`, `length`) from CPU addresses (`load`, `entry`);
- the declared used length from the actual stream length/device-capacity padding;
- base DS fields from extended DSi fields;
- known fields from reserved/unknown bytes that must round-trip untouched.

The devkitPro `libnds` public header independently exposes `tNDSHeader`,
`tDSiHeader`, and `tNDSBanner`, including ARM9i/ARM7i ranges, digest/hash regions,
title IDs, save sizes, age ratings, HMACs, and the RSA signature area.
[libnds `memory.h` at commit `84e6082`](https://github.com/devkitPro/libnds/blob/84e6082ce27c87ed218fb369a9944644aa2243a6/include/nds/memory.h#L119-L246)
The upstream ndstool packed extended header is likewise explicit in
[`header.h`](https://github.com/devkitPro/ndstool/blob/76e8b681bb225d945a48852821e03114e6c7ce1c/source/header.h#L1-L150).
GBATEK's maintained reference also documents the
[DSi extended cartridge header](https://problemkaputt.de/gbatek.htm#dsicartridgeheader).
DSi parsing should be fully lossless even when some fields remain semantically
unknown; signing or authentication claims need separate, fixture-backed evidence.

### NitroFS: FNT, FAT, and file IDs

NitroFS is a pair of related structures, not PC FAT. The cartridge FAT is an
array of 8-byte start/end ROM ranges indexed by file ID. The FNT has 8-byte
directory records plus variable sub-tables. Root is directory `0xF000`, child
directory IDs start at `0xF001`, each directory gives its first file ID, file IDs
increment through file entries, names are case-sensitive ASCII with a maximum
length of 127, and directory entries carry a child directory ID.
[GBATEK NitroROM filesystem](https://www.akkit.org/info/gbatek.htm#dscartridgenitroromfilesystem)

Consequences for the API and writer:

- A file needs both a path and stable numeric `FileId`; overlays refer to the ID.
- FNT traversal must reject cycles, duplicate/invalid directory IDs, out-of-range
  sub-table offsets, truncated names, invalid type/length bytes, and file-ID
  overflow before walking or allocating.
- FAT ranges must be ordered (`start <= end`), within the source, and safe under
  checked 64-bit arithmetic even though stored fields are 32-bit.
- Editing path order can change file IDs. Default edits should retain IDs where
  possible; deterministic rebuild mode must document its ID-ordering policy.
- Empty or malformed FNT/FAT should remain inspectable in lenient mode, while
  strict mode must fail with precise offset-bearing diagnostics.

### Overlays

Each 32-byte ARM9/ARM7 overlay record stores overlay ID, RAM address/size, BSS
size, static-initializer bounds, referenced file ID, and a reserved word.
[GBATEK overlay table](https://www.akkit.org/info/gbatek.htm#dscartridgenitroromfilesystem)
Overlay payloads live in FAT entries and may be absent from the named FNT tree.
They must therefore be first-class `NdsOverlay` objects, not guessed filenames.
The model should expose the referenced `NdsFile` when valid and retain the raw
file ID plus a validation error when invalid.

That distinction fixes a real upstream defect: devkitPro 2.3.1 extraction uses
`overlayEntry.id` as the FAT index instead of `overlayEntry.file_id`.
[Faulting source](https://github.com/devkitPro/ndstool/blob/76e8b681bb225d945a48852821e03114e6c7ce1c/source/ndsextract.cpp#L209-L223),
[reported regression](https://github.com/devkitPro/ndstool/issues/16)
Tests must cover unequal overlay and FAT file IDs. Pokémon Mystery Dungeon: Blue
Rescue Team is the reported case, but the committed fixture should be synthetic
and redistributable.

### Banner/icon/title

The original banner contains a version, CRC, 32x32 tiled 4bpp icon, 16-color
palette, and six 128-code-unit title fields (Japanese, English, French, German,
Italian, Spanish).
[GBATEK icon/title structure](https://www.akkit.org/info/gbatek.htm#dscartridgeicontitle)
Upstream additionally recognizes banner versions 1, 2, 3, and `0x0103` with
sizes `0x840`, `0x940`, `0xA40`, and `0x23C0` respectively.
[`CalcBannerSize`](https://github.com/devkitPro/ndstool/blob/76e8b681bb225d945a48852821e03114e6c7ce1c/source/banner.h#L17-L27)

The core library should expose raw tile/palette data and a platform-neutral RGBA
pixel conversion. Bitmap/PNG/GRF codecs belong in a separate optional package or
adapter so the core stays pure, cross-platform, and free of `System.Drawing`.

### CRCs, secure area, and encryption

The DS BIOS CRC-16 accepts an initial value (usually `0xFFFF`) and processes the
data with the reflected `0xA001` polynomial family.
[GBATEK `GetCRC16`](https://www.akkit.org/info/gbatek.htm#biosmiscfunctions)
Header, logo, banner, and secure-area CRCs are separate named operations and
should never be represented by an ambiguous `FixCrc()` method.

The secure area is `0x4000..0x7FFF` when ARM9 begins inside it; its first 2 KiB
may be KEY1 encrypted, with a secure-area ID and its own CRC-covered content.
[GBATEK secure area](https://www.akkit.org/info/gbatek.htm#dscartridgesecurearea)
Upstream's transformation is implemented in
[`encryption.cpp`](https://github.com/devkitPro/ndstool/blob/76e8b681bb225d945a48852821e03114e6c7ce1c/source/encryption.cpp#L293-L571).
The .NET API must separate detection, validation, decrypt-to-new-output, and
encrypt-to-new-output. It should not modify the caller's file in place by default.

## Original CLI feature inventory

The old CLI allows actions to be combined; the ROM filename is supplied once and
actions run in argument order. Addresses may use a `0x` prefix.
[`Help`](https://github.com/devkitPro/ndstool/blob/76e8b681bb225d945a48852821e03114e6c7ce1c/source/ndstool.cpp#L138-L168)

| Switch | Upstream behavior | Library interpretation |
|---|---|---|
| `-i`, `-v [roms_rc.dat]` | Print header, checksums/warnings, hashes, optional release-list match | `Inspect()`, `Validate()`, structured report; external metadata provider |
| `-f` | Rewrite header CRC (and DSi signature-related data in newer paths) | Explicit checksum repair plan and atomic save |
| `-l`, `-v` | List NitroFS names, optionally offsets/sizes | Enumerate `NdsFileSystem` entries with ranges |
| `-x` | Extract selected components and/or NitroFS | Export APIs that accept streams/paths and overwrite policy |
| `-w masks...` | Limit NitroFS extraction using `*`/`?` masks | Predicate/glob selection; never use as path validation |
| `-9`, `-7` | ARM9/ARM7 input or output | `Arm9`/`Arm7` executable regions |
| `-9i`, `-7i` | DSi ARM9i/ARM7i input or output (parser supports these although help omits them) | Extended executable regions |
| `-y9`, `-y7`, `-y dir` | Overlay tables and overlay payload directory | Overlay collections plus raw import/export |
| `-d dir` | NitroFS root directory | Import/export filesystem tree |
| `-t file.bin` | Raw banner input/output | `NdsBanner.Parse/Write` |
| `-b file.bmp "a;b;c"` | Build icon/title banner; source also accepts GRF | Optional image/GRF adapters and typed localized titles |
| `-o file.bmp/file.bin` | Cartridge logo creation input; extraction is raw | Caller-provided raw/logo conversion adapter |
| `-h file.bin` / `-h size` | Header template or header-size choice | Template/import and `HeaderLayoutPolicy` |
| `-g`, `-m` | Game code, maker, title, ROM version | Validated header editor properties |
| `-r9/-r7`, `-e9/-e7` | CPU load and entry addresses | Executable descriptors with range validation |
| `-n [L1] [L2]` | Cartridge latency/control tuning during creation | Typed cartridge timing options |
| `-c` | Build DS/DSi image, accepting BIN or ELF and optional filesystem/banner/overlays | `NdsRomBuilder` and `NdsWriteOptions` |
| `-s`, `-sd`, `-se`, `-sE` | Auto/directed secure-area decrypt/encrypt and table/test-pattern placement | Explicit secure-area service and compatibility profile |
| `-u`, `-z`, `-a`, `-p`, `-q` | DSi title-ID-high, SCFG mask, access, application, and WRAM mapping settings | Typed DSi build options |
| `-k` | Append ARM7 trainer/hook while attempting to preserve whole-ROM CRC32 | `Legacy` opt-in only; fully characterize before claiming support |
| `-?` | General or switch-specific help | Separate CLI package |

The active BlocksDS surface adds `-fh`, `-fb`, `-V`, `-ba`, `-bi`, `-bt`, `-uc`,
`-pass`, `-nopass`, PNG/GIF input, and multiple input directories after `-d`.
These should map to the same object model, not a second implementation.

Notable upstream quirks to avoid preserving as API behavior: `-k` is labeled
"currently not tested" in help; the `-q` help syntax mistakenly says `-m`; errors
frequently terminate the process; extraction trusts ROM names; and some options
are supported but absent from help. Compatibility tests may preserve accepted CLI
syntax in a CLI facade, but the object model should not reproduce these defects.

## Capability matrix for NdsForge.NET

Priority `P0` is required for the first production-ready package, `P1` completes
full upstream parity, and `P2` is a high-value modern extension.

| Capability | Upstream | Proposed .NET surface | Done evidence | Priority |
|---|---|---|---|---|
| Open from path/seekable stream | Path only | `NdsRom.Open/OpenAsync` with ownership options | Path, `MemoryStream`, short-read stream tests | P0 |
| Header/region inspection | Printed report | Strongly typed base + DSi header and `NdsRegionMap` | Golden field tests and unknown-byte round trip | P0 |
| Validation/diagnostics | Mixed warnings/exits | `Validate(NdsValidationOptions)` returns coded diagnostics | Corrupt/truncated/fuzz corpus never crashes/hangs | P0 |
| NitroFS read/list/open | Yes | Tree plus lookup by path and file ID; zero-copy/stream reads | Synthetic nested tree and malicious FNT/FAT tests | P0 |
| Safe extraction | Yes, path trust | Export with containment, overwrite, symlink, and cancellation policies | Traversal/symlink/duplicate-name tests | P0 |
| Component extraction | ARM binaries, tables, banner, header, logo | Raw region/export APIs | Byte-exact goldens for each region | P0 |
| Header/logo/banner/secure CRC | Yes | Named calculate/verify/repair operations | GBATEK vectors and mutation tests | P0 |
| Integrity/authenticity report | Partial CRC/SHA-1/Download Play checks | Structured CRC, SHA, Download Play, and DSi digest-tree checks with clear trust semantics | Known-answer vectors and tamper-at-each-layer tests | P0 |
| Edit/replace NitroFS files | Rebuild from directory | `NdsRomEditor.ReplaceFile` preserving IDs | Grow/shrink/same-size round trips | P0 |
| Add/remove/move/rename files and directories | Indirect via rebuild | Editor operations with collision/ID policy | Property-based tree rebuild tests | P0 |
| Deterministic DS image build | Yes | Builder + deterministic/preserve/compact layout profiles | Rebuild twice => identical bytes; emulator boot acceptance test | P0 |
| Overlay parsing/editing | Extract/build | ARM9/ARM7 typed overlay collections and file links | File-ID linkage and invalid-reference tests | P0 |
| Banner read/edit/render | Raw/BMP/GRF build | Typed languages, raw icon/palette, RGBA conversion | Versions 1/2/3/0x103 round trips | P0 |
| Modern/multilingual banner import | BlocksDS BMP/GIF/PNG, static/animated, per-language text | Optional codec adapter feeding `NdsBanner` | Static/animated and every-language goldens | P1 |
| Multiple NitroFS build roots | BlocksDS | Ordered merge sources with explicit collision policy | Merge/collision/determinism tests | P1 |
| ELF32 ARM ingestion | Yes | `NdsElfImageReader` behind builder adapter | paddr/vaddr, BSS, malformed PHDR tests | P1 |
| ELF overlay ingestion | Yes in 2.3 | Overlay-aware ELF import | Differential fixtures for ARM7/ARM9 | P1 |
| Secure-area transform | Yes | Detect/validate/encrypt/decrypt to destination | Known-answer and reversible transform tests | P1 |
| DSi extended read/write/build | Yes | Lossless `DsiHeader`, ARM9i/ARM7i, digest regions/options | Extended-header/banner fixtures; no false signature claims | P1 |
| DSi/TWL digest hash-tree validation | Missing upstream | Sector/block/master digest validation independent from parsing | Corrupt each level/range; close upstream gap #13 | P1 |
| Header templates/timing profiles | Yes | Typed compatibility profiles plus raw template | ndstool-compatible build fixtures | P1 |
| ARM7 hook/trainer operation | Legacy, self-described untested | Isolated `NdsLegacyArm7Hook` package or namespace | Independently specified invariants and emulator fixture | P1 |
| Release database matching | External legacy data file | `IRomMetadataResolver` extension, not core parser | Fake provider contract tests | P1 |
| Manifest/JSON inspection | No | Stable DTO/export package | Schema snapshot tests | P2 |
| Diff/change plan | No | Editor exposes semantic + physical change plan before save | Deterministic diff tests | P2 |
| Trim/untrim/padding control | Implicit build padding | Explicit used-length/device-capacity policy | Hardware/emulator-compatible boundary tests | P2 |
| In-place patch optimization | Some actions mutate | Optional only after safe full-save path | Crash-safety/backup tests; never default | P2 |

## Recommended public API shape

Use a small, discoverable core rather than one class per CLI option:

- `NdsRom`: read-only parsed document; owns or borrows an `IRomDataSource`.
- `NdsHeader` / `DsiHeader`: lossless typed views with raw reserved data retained.
- `NdsExecutable`: kind (`Arm9`, `Arm7`, `Arm9i`, `Arm7i`), ROM range, load and
  entry address, and a method to open content.
- `NdsFileSystem`, `NdsDirectory`, `NdsFile`: tree, path, stable IDs, FAT ranges,
  lookup, enumeration, and content streams.
- `NdsOverlayTable` / `NdsOverlay`: ARM target, memory metadata, raw file ID, and
  optional resolved file reference.
- `NdsBanner`: versioned raw model, localized titles, icon palette/tiles, and RGBA
  conversion without an OS graphics dependency.
- `NdsValidationResult` and `NdsDiagnostic`: severity, stable code, message,
  absolute ROM offset/range, and related path/file/overlay ID.
- `NdsRomEditor`: explicit mutable session with `ReplaceFile`, `AddFile`,
  `Remove`, `Move`, header/banner edits, `GetChangePlan`, and validation.
- `NdsRomBuilder`: create images from executable sources and an in-memory or disk
  filesystem tree.
- `NdsWriteOptions`: layout profile, file-ID policy, alignment, padding,
  deterministic mode, checksum policy, unknown-data policy, and compatibility
  target. `Save` writes a new stream/file by default.
- `NdsSecureArea`: pure detection/validation/transformation methods with explicit
  destination buffers/streams.

Use exceptions only for failed operations (`NdsFormatException`,
`NdsValidationException`, `NdsWriteException`, ordinary I/O/cancellation). Expected
format findings belong in diagnostics. Public offsets and lengths should be
`long`/`ulong` or range value types; convert to stored 32-bit values only after
checked validation.

The package should offer synchronous stream APIs because binary parsing is often
in-memory, plus async file/content copy and save APIs where I/O can be substantial.
On .NET 10, `RandomAccess` provides thread-safe offset-based file I/O, while stream
implementations remain necessary for memory, archive, and caller-owned sources.
[Microsoft `RandomAccess`](https://learn.microsoft.com/en-us/dotnet/api/system.io.randomaccess?view=net-10.0)

## Write and compatibility policies

Three explicit modes remove much ambiguity:

1. **Preserve**: retain unknown fields, reserved bytes, file IDs, original ordering,
   and unchanged physical ranges where practical. This is the default for editing.
2. **Deterministic rebuild**: lay out every section from the object model under a
   documented stable ordering/alignment/padding algorithm. This is the default for
   new images and reproducible builds.
3. **ndstool 2.3.1 compatibility**: reproduce upstream layout conventions where
   projects depend on byte-level behavior. Confine quirks to this named profile.

Never silently "repair" while opening. Validation is read-only; repair produces a
change plan; save commits it. Writes to filesystem paths should use a sibling
temporary file and atomic replacement where the platform supports it. Preserve
the input if validation or writing fails.

## Security requirements

- Checked arithmetic for every `offset + length`, count multiplication, alignment,
  and conversion; reject ranges outside both declared and physical ROM bounds.
- Configurable maximum ROM size, directory count/depth, file count/name length,
  overlay count, banner size, and allocation size.
- Iterative or depth-limited FNT traversal with cycle detection.
- Extraction canonicalizes the destination root and each result, rejects rooted
  names, separators, `.`/`..`, alternate data streams/platform tricks, collisions,
  and any escape from root. Symlink/reparse-point policy must be explicit.
- No overwrite by default; never follow an existing symlink for output.
- Parsing and validation have no filesystem side effects.
- Cancellation on async extraction/build and periodic cancellation checks on long
  hashing/copying loops.
- No ambient current-directory, environment-variable, console, or process-exit
  dependencies in the library.
- Fuzz the header, FNT/FAT, overlay, banner, and ELF parsers. A malformed image may
  yield a controlled exception/diagnostic, never unbounded memory, stack overflow,
  path escape, hang, or process termination.

## Project/package layout recommendation

- `NdsForge` — pure .NET 10 core model, parsing, editing, writing, CRC, banner raw
  conversion, secure-area primitives; minimal dependencies.
- `NdsForge.Elf` — ELF32 ingestion if keeping it out of the core improves cohesion.
- `NdsForge.Images` — optional PNG/BMP/GRF adapters using a reviewed cross-platform
  codec; no image dependency leaks into core public types.
- `NdsForge.Cli` — modern commands (`inspect`, `validate`, `extract`, `build`,
  `replace`, `banner`, `secure-area`) and optional legacy switch compatibility.
- `NdsForge.Tests` — unit/property/fuzz regression tests and synthetic fixtures.
- `NdsForge.CompatibilityTests` — optional black-box comparison with an externally
  obtained/built ndstool 2.3.1; do not redistribute its executable casually.

Microsoft recommends SDK-style projects and `dotnet pack` for NuGet packages, and
also recommends reviewing and minimizing library dependencies.
[NuGet library guidance](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/nuget),
[dependency guidance](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/dependencies)

## Test and acceptance strategy

Use generated, redistributable fixtures as the committed corpus. Construct the
smallest valid DS image, then parameterize nested/empty NitroFS trees, all banner
versions, overlays, ARM sections, alignments, padding sizes, and DSi extended
fields. Store fixture source descriptions alongside expected bytes so goldens are
auditable rather than mysterious ROM blobs.

Required test layers:

1. **Primitive vectors**: little-endian fields, alignment, CRC-16, hashes, tiled
   icons, palette conversion, glob matching, secure-area known answers.
2. **Parser goldens**: every header/banner version, zero-length tables, multiple
   overlays, unnamed overlay FAT entries, and unknown-byte retention.
3. **Round trips**: parse/write without edits under preserve mode; edit/save/reopen;
   deterministic build twice; extraction followed by rebuild under documented
   normalization.
4. **Mutation/property tests**: random valid filesystem trees and file sizes;
   add/remove/rename/move while checking IDs, paths, FNT, FAT, and overlays.
5. **Negative corpus**: truncation at every structural boundary, integer overflow,
   overlapping regions, cycles, invalid names/IDs, decompression or allocation
   bombs if codecs are present, hostile extraction paths, and malformed ELF.
6. **Differential tests**: where licensing permits, execute upstream ndstool 2.3.1
   as an external oracle and compare inspection/extraction/build behavior. Record
   intentional deviations, especially security fixes and deterministic ordering.
7. **Runtime quality**: fuzz campaigns, cancellation, parallel reads, handle/stream
   ownership, Windows/Linux/macOS path behavior, large sparse test files, package
   clean install-and-run acceptance test, API compatibility check, and `dotnet pack` validation.
8. **Integration**: boot independently generated homebrew images in at least two
   actively maintained DS emulators and, when the owner can lawfully do so, run a
   hardware boot acceptance test. Emulator success supplements rather than
   replaces structural assertions.

Production-ready acceptance means every P0/P1 matrix row is implemented or has a
written, owner-approved exclusion; public APIs are documented; malformed-input
and extraction security suites pass; all shipped assets are redistributable; the
NuGet package builds reproducibly with symbols/source metadata; and the CLI is a
consumer of the public library rather than a privileged internal path.

## Decisions to make before broad implementation

1. **License gate**: GPLv3-compatible distribution versus a legally reviewed,
   independently authored implementation strategy. This must be explicit before
   adapting any upstream code or tables.
2. **Scope claim**: whether "full" means DS plus DSi read/write/build. Upstream
   2.3.1 supports DSi homebrew construction, so parity argues yes, but commercial
   DSi signing/authentication must not be claimed without evidence and lawful keys.
3. **Legacy hook**: implement as isolated, opt-in parity or exclude it with an
   explicit rationale. Do not let an upstream "untested" feature weaken the core.
4. **Image codecs**: choose an optional cross-platform codec dependency or expose
   raw RGBA only in core and leave file formats to adapters.
5. **Compatibility default**: prefer safe deterministic/preserve behavior and make
   ndstool quirks an explicit profile, not the default personality of the API.

## Primary-source index

- [devkitPro/ndstool source at 2.3.1](https://github.com/devkitPro/ndstool/tree/76e8b681bb225d945a48852821e03114e6c7ce1c)
- [BlocksDS ndstool source snapshot](https://github.com/blocksds/ndstool/tree/4d8ef3e7451dd0c633837318612e13dbe932b52f)
- [BlocksDS ndstool build documentation](https://blocksds.skylyrac.net/docs/internal/build_process/)
- [BlocksDS changelog](https://blocksds.skylyrac.net/docs/changelog/)
- [ndstool CLI/action dispatcher](https://github.com/devkitPro/ndstool/blob/76e8b681bb225d945a48852821e03114e6c7ce1c/source/ndstool.cpp)
- [ndstool extraction implementation](https://github.com/devkitPro/ndstool/blob/76e8b681bb225d945a48852821e03114e6c7ce1c/source/ndsextract.cpp)
- [ndstool image creation implementation](https://github.com/devkitPro/ndstool/blob/76e8b681bb225d945a48852821e03114e6c7ce1c/source/ndscreate.cpp)
- [ndstool banner implementation](https://github.com/devkitPro/ndstool/blob/76e8b681bb225d945a48852821e03114e6c7ce1c/source/banner.cpp)
- [ndstool ELF implementation](https://github.com/devkitPro/ndstool/blob/76e8b681bb225d945a48852821e03114e6c7ce1c/source/elf.cpp)
- [ndstool secure-area implementation](https://github.com/devkitPro/ndstool/blob/76e8b681bb225d945a48852821e03114e6c7ce1c/source/encryption.cpp)
- [ndstool ARM7 hook implementation](https://github.com/devkitPro/ndstool/blob/76e8b681bb225d945a48852821e03114e6c7ce1c/source/hook.cpp)
- [ndstool GPLv3 license text](https://github.com/devkitPro/ndstool/blob/76e8b681bb225d945a48852821e03114e6c7ce1c/COPYING)
- [devkitPro/libnds DS/DSi public header structs](https://github.com/devkitPro/libnds/blob/84e6082ce27c87ed218fb369a9944644aa2243a6/include/nds/memory.h#L119-L246)
- [GBATEK DS cartridge header, secure area, banner, NitroFS, overlays, and CRC](https://www.akkit.org/info/gbatek.htm#dscartridgesencryptionfirmware)
- [GBATEK DSi extended cartridge header](https://problemkaputt.de/gbatek.htm#dsicartridgeheader)
- [Microsoft .NET 10 `RandomAccess`](https://learn.microsoft.com/en-us/dotnet/api/system.io.randomaccess?view=net-10.0)
- [Microsoft NuGet library guidance](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/nuget)
- [Microsoft library dependency guidance](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/dependencies)
- [Nintendo intellectual-property FAQ](https://en-americas-support.nintendo.com/app/answers/detail/a_id/55888/~/intellectual-property-%26-piracy-faq)
