# Changelog

All notable changes to NdsForge.NET are documented here. The project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

### Added

- Convert RGBA pixels into deterministic four-bit indexed graphics with explicit
  transparency, RGB packing, palette limits, overflow reduction or rejection,
  fixed-palette mapping, and native tile/palette encoding. Feed static and all
  DSi animated Banner slots through the existing core builder. Keep native
  quantization in the dependency-light graphics package with no host image codec.
- Read and rebuild Wi-Fi utility archives with bounded filename/allocation tables,
  stable identities, exact no-op preservation, payload replacement, and file or
  directory renaming. Preserve unnamed, empty, and shared allocations; expose
  independent canonical table/payload alignment without inferring SDK versions.
- Add verified CLI `build` for edited workspaces with deterministic output, explicit
  cartridge capacity/padding and DS/DSi authentication policies, and atomic output
  publication outside the workspace. Reject missing inputs, inapplicable policies,
  detected links, and ambiguous device/alternate-stream output names.
- Import supported workspace payload edits into detached structural builders with
  bounded materialization, strict original-snapshot validation, and explicit
  rejection of changed layout tables or unrepresented allocation relationships.
  Preserve program footers and carrier reservations, and synchronize supported
  overlay replacements. Structural building remains separate from byte-exact pack.
- Preserve and expose NAND partition boundaries in headers, builders, manifests,
  workspace inventories, semantic differences, and CLI inspection. Keep nominal
  capacity large enough for declared partitions without forcing file expansion;
  reject conflicting structural layouts and partition-crossing preservation writes,
  and diagnose ambiguous
  declarations without treating partition addresses as missing file data.
- Export self-contained versioned image workspaces and verify byte-exact packing
  through the API and `unpack`/`pack` commands. Retain all FAT allocations, raw
  ordering tables, carrier bytes, and a full preservation snapshot. Reject edited
  or missing inputs, inconsistent metadata, unsafe paths, and detected links
  before transactional output publication; structural workspace edits are not
  included in this preservation workflow.
- Add source-preserving resize APIs and a `resize` CLI command with explicit
  preserve, trim, capacity-pad, and exact-length modes. Verify removed padding by
  default; require explicit discard for unclassified trailing bytes. Preserve
  headers, authentication coverage, and known post-used content, and verify every
  retained/output byte with bounded buffers and transactional path writes.
- Request an explicit cartridge capacity independently from physical output size,
  or pad structural builds to the selected capacity without changing used-size
  declarations. Reject undersized, nonrepresentable, digital-carrier, and
  contiguous-memory-incompatible requests before destination mutation.
- Expose physical size, common and DSi used-size declarations, checked device
  capacity, declared content extent, and unclassified trailing ranges separately.
  Include trailers and authentication coverage, and diagnose missing content or
  unrepresentable capacity exponents instead of wrapping shifts.
- Apply DSi cartridge access boundaries separately from digital-SRL packing; retain
  opaque TWL reservation bytes, reserve the ARM9i secure window, and place optional
  digest tables inside common content. Diagnose malformed reservations and protect
  them from overlapping preservation edits.
- Distinguish cartridge, digital-SRL, and unresolved carriers independently of
  execution mode; retain opaque post-header material and reject contradictory or
  malformed carrier declarations with explicit diagnostics.
- Preserve digital capacity metadata and opaque carrier bytes during structural
  builds, support explicit empty DSi-mode program tuples, and avoid treating
  digital program data as a cartridge KEY1 secure area. Add five private digital
  fixture identities with neutral metadata and program-payload expectations.

- Preflight preservation payload, banner, and trailer writes against other stored
  components, rejecting overlapping appends or aliased allocations before the
  destination is changed, including DSi programs beyond the common used boundary.

- Detect bounded post-used Download Play signature trailers; preserve their exact
  bytes across no-op copies, structural rebuilds, and relocated edits, with explicit
  truncation and potentially stale-signature diagnostics. No trailer signing or
  cryptographic verification is implied.
- Preserve late-DS build metadata and relocate program-parameter pointers; require
  explicit authentication preservation, removal, or keyed regeneration for
  structural builds and preservation edits of authenticated images.
- Coordinate classic overlay records, ARM9 recompression, final physical coverage,
  late-DS HMACs, and optional verified header signing. Report retained unverified
  fields and missing signing authority through build/save diagnostics.
- Model late-generation DS authentication headers, program feature flags, stored
  HMAC/signature fields, canonical ARM9 SDK parameter tables, and typed SDK
  footers without requiring DSi unit mode.
- Expose overlay compression, authentication, and reserved control bits on both
  loaded overlays and build definitions.
- Parse classic-DS Download Play HMAC tables from plain or BLZ ARM9 programs,
  validate embedded or caller-supplied keys, and atomically repair records when
  overlays are replaced or deterministically recompressed.
- Expose late-DS aggregate overlay authentication coverage and calculate its
  HMAC with caller-supplied credentials, including FAT order, physical sector
  padding, and the shared payload budget. Reject unsupported sparse selections.
- Calculate late-DS program HMACs from explicit header and program byte inputs,
  and banner HMACs for all static and animated banner versions with a separately
  supplied key; no automatic secure-area transformation or trust is implied.
- Add opt-in late-DS HMAC and RSA validation with independent caller credentials,
  verified encrypted/decrypted secure-area normalization, and explicit missing-key,
  unsupported-layout, and digest/signature-mismatch diagnostics.
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

- Correct cartridge RSA signing and verification to use type-one padded raw
  SHA-1 instead of the incompatible ASN.1-wrapped signature encoding. Validate
  the complete padding block and blind and check private signing operations.
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
