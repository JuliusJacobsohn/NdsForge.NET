# Changelog

All notable changes to NdsForge.NET are documented here. The project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

### Added

- Model late-generation DS authentication headers, program feature flags, stored
  HMAC/signature fields, canonical ARM9 SDK parameter tables, and typed SDK
  footers without requiring DSi unit mode.
- Expose overlay compression, authentication, and reserved control bits on both
  loaded overlays and build definitions.
- Parse classic-DS Download Play HMAC tables from plain or BLZ ARM9 programs,
  validate embedded or caller-supplied keys, and atomically repair records when
  overlays are replaced or deterministically recompressed.
- Add the dependency-free `NdsForge.Nitro` companion package and independently
  implemented BLZ inspection, bounded decompression, and deterministic
  compression APIs.
- Add shared Nitro compression inspection and dispatch, bounded LZ10, LZ11,
  four- and eight-bit Huffman, and run-length decoders, plus deterministic LZ10,
  LZ11, and run-length encoders.
- Add bounded NARC parsing, stable-ID and hierarchical filename models,
  byte-exact preservation writes, payload replacement, and deterministic aligned
  reconstruction in `NdsForge.Nitro`.
- Add conservative BMG message-bundle reading with both byte orders, all standard
  encoding declarations, opaque metadata and auxiliary sections, lossless typed
  control sequences, and byte-exact preservation.
- Generate and validate the alternate DSi ARM9 HMAC whose input excludes the
  first 16 KiB secure area.
- Add the optional `NdsForge.Graphics` feature package with dependency-free
  RGBA/BGR555 colors and verified NCLR parsing, creation, editing, preservation,
  PCMP mapping, and canonical reconstruction.
- Add verified NCGR character graphics and NSCR screen maps with 4/8-bpp and
  tiled/linear storage, affine/text/extended entries, palette selection, flips,
  lossless editing, canonical writing, and dependency-free RGBA composition.
- Add verified NCER cell-bank parsing, exact OAM word preservation and typed
  projection, lossless object replacement, and deterministic reconstruction.
- Add bounded, read-only NANR animation-bank parsing with exact sequence,
  frame-descriptor, and NCER cell-reference metadata plus byte preservation.
- Add verified NFTR bitmap fonts with typed FINF metadata, indexed CGLP glyphs,
  CWDH metrics, all three CMAP methods, exact preservation edits, and
  deterministic canonical reconstruction.
- Complete common and DSi header semantics for optional debug executables,
  territory and launch policy, crypto and access capabilities, application
  policy, MBK/WRAM registers, shared-data metadata, EULA revision, and all
  parental-rating slots while preserving unknown and reserved bits.
- Preserve debug executable bytes through structural builds, validate malformed
  debug ranges, and include the expanded header semantics in JSON manifests and
  semantic image comparison.

### Fixed

- Validate every reachable Huffman tree branch, including unused branches and
  four-bit leaves, before decoding; shared forward subtrees remain bounded.
- Accept bounded zero alignment fill at the declared end of NCLR, NCER, and
  NANR resources while continuing to reject nonzero or excessive fill.
- Preserve unknown NCER character-mapping values through parsing and writing.

## 1.0.1 - 2026-08-18

### Changed

- Added a dedicated NdsForge visual identity, with a compact dual-screen package
  icon for both NuGet packages and a matching `FORGE` repository wordmark.
- Streamlined hosted automation to a complete Ubuntu release gate and focused
  Windows portability run. Removed duplicate macOS, documentation, artifact,
  and scheduled secret-scan work while retaining dependency auditing in CI.
- Reworked the package README into a concise developer-facing introduction and
  moved detailed guidance into focused documentation pages.
- Added strict, deployable API documentation with broken-reference and warning
  checks.
- Added locked dependency resolution, public API compatibility tracking,
  Source Link symbols, package-content validation, clean-consumer tests, and
  enforceable coverage and source-size gates.
- Added cross-platform CI, documentation deployment, private-corpus compatibility
  automation, and an explicitly authorized release workflow for both NuGet
  packages.

## 1.0.0 - 2026-08-06

First stable release.

- Parse DS, DSi-enhanced, and DSi-exclusive headers into immutable typed models.
- Navigate NitroFS directories, named files, allocations, programs, overlays,
  SDK footers, and static or animated banners with stream-first payload access.
- Validate bounds, relationships, CRCs, secure areas, DSi digest trees, HMACs,
  and RSA signatures with structured diagnostics and explicit trust inputs.
- Safely extract selected components and NitroFS files under explicit overwrite,
  filtering, traversal, collision, and reparse-point policies.
- Make verified preservation edits or deterministic structural rebuilds without
  mutating the source image.
- Build DS and DSi images from binary/ELF programs, overlays, banners, directory
  trees, caller-supplied metadata and integrity credentials.
- Inspect and transform KEY1 secure areas and DSi modcrypt regions.
- Capture stable JSON manifests and compare images semantically and physically.
- Reproduce deterministic ndstool 1.50.3 build and legacy ARM7-hook output where
  the historical tool provides a valid byte-level oracle.
- Ship the `ndsforge` .NET tool for inspection, validation, listing, extraction,
  file replacement, manifest capture, and semantic comparison.
