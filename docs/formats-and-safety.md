# Formats, compatibility, and safety {#formats_and_safety}

## Supported image families

NdsForge reads and models original Nintendo DS images, DSi-enhanced images, and DSi-exclusive images. Support covers interpreted headers, programs, overlays, NitroFS tables and payloads, banner families, secure-area state, and the DSi regions represented by the public API. Late-generation DS images that opt into DSi-era banner or program authentication retain their complete 0x1000-byte header and expose the declared HMAC and signature fields separately from the DSi unit model.

Common headers expose the optional debug executable as an offset, byte length, load address, and bounded region. Structural builders represent it as an owned `NdsDebugProgramDefinition`, assign its physical location with the other components, and verify both metadata and bytes after rebuilding. Manifests hash the executable separately from the header. A header with only one of offset or length, or with a region outside the physical image, receives diagnostic `NDS1109`.

Territory, launch, crypto, access-control, application-policy, memory-bank, shared-data, EULA, and parental-rating fields have typed projections alongside their complete native values. Typed builder setters change only their assigned bits where a field contains unassigned bits; raw properties and preserved header templates retain everything else. `NdsDsiAgeRating` keeps the complete authority byte while separately exposing age, applicability, prohibited/pending state, and the unassigned bit. Manifests include the corresponding raw scalars and canonical lowercase hexadecimal strings for MBK, shared-data-size, and rating arrays, so semantic comparison detects these edits without embedding payload data.

Canonical ARM9 SDK parameter tables are recognized only when both byte-order markers are present and the complete fixed prefix lies inside the program. This prevents a legacy tool-generated footer placeholder or an arbitrary in-range pointer from being reported as SDK metadata. Stored authentication bytes are evidence from the image, not proof of authenticity; verification still requires caller-selected trust material.

Classic-DS overlays whose control record enables Download Play authentication are linked to the positional 20-byte records inside decoded ARM9. `NdsImage.Arm9OverlayAuthentication` reports whether the SDK footer pointer is complete, missing, or outside the decoded program, as well as plain-versus-BLZ storage and the retained prefix. Validation accepts an explicit key through `SetArm9OverlayHmacKey`; without one, it considers a conventional 64-byte ARM9 key block usable only after that candidate reproduces every flagged record. A marked block is never trusted merely because its prefix looks familiar.

Structural imports retain verified table and key state privately. `NdsImageBuilder.ReplaceOverlay` can preserve stored bytes, store decoded bytes directly, or apply deterministic BLZ encoding. Before layout assigns offsets, the builder updates the stored-size and compression fields, hashes the exact stored overlay payloads, patches decoded ARM9, re-encodes ARM9 when required, and repairs its SDK compressed-end address. A missing key, stale source table, bad footer pointer, unrepresentable BLZ output, or compressed-end field outside the verbatim prefix fails before output is written. The local preservation editor deliberately rejects authenticated ARM9 overlay replacement because it cannot relocate a newly compressed ARM9 atomically; use a structural builder for that operation.

`NdsDsAuthentication.GetOverlayHashRegions` exposes the separate late-DS aggregate coverage: the ARM9 overlay table, the leading FAT records, and sector-rounded payload prefixes in FAT order, sharing a 512 KiB payload budget. `ComputeOverlayHmac` hashes those regions with an explicitly supplied late-DS key; the ARM9-embedded per-overlay key is not a substitute. Physical padding is part of this calculation. The supported layout requires ARM9 overlays to reference each leading FAT entry exactly once, although their table order may differ; sparse or duplicate selections and coverage beyond physical EOF are rejected. With no overlays, the raw API calculates HMAC of empty input, while an absent serialized aggregate field is twenty zero bytes. These calculation primitives do not mutate an image; coordinated writes use the explicit policy below.

Late-DS structural imports retain opaque extension bytes and anchor SDK parameter pointers relative to their programs through `NdsDsBuildMetadata`. An imported authenticated recipe requires a deliberate `Integrity` policy before building; preservation edits of an authenticated image likewise require `NdsWriteOptions.DsIntegrity`. A no-op editor save with no authentication policy copies the source exactly without requiring keys. The three policies are:

- `PreserveStored` retains existing authentication bytes and reports `NDS1540`: they are unverified and changes to covered bytes may make them stale.
- `Unauthenticated` clears only the late-DS HMAC fields, RSA field, and their two declaration bits, leaving unrelated extension bytes and feature bits untouched. It does not remove or bypass classic per-overlay authentication records.
- `CreateHmacSha1` accepts separate program/aggregate and banner keys, plus KEY1 material for program authentication. After classic records and program recompression are finalized, the writer assigns layout, ensures physical sector coverage, updates secure-area and header CRCs, calculates component HMACs, and optionally signs the completed header. Private ARM9 overlay allocations lead the FAT when aggregate regeneration requires it. Missing credentials, unsupported secure-area representations, and unsupported allocation selections fail explicitly.

A signing provider must be paired with its public verification key; a bad generated signature fails even if ordinary output verification is disabled. HMAC generation without signing authority clears the old RSA field and reports `NDS1542`, rather than retaining a stale signature. Build and save result `Diagnostics` expose these decisions. Path writes publish only after completion; stream writes may remain partial if a signer, I/O operation, or cancellation fails. Providers remain caller-owned. Regeneration establishes compatibility under caller-supplied keys, not acceptance by retail hardware or trust in a publisher's original signature.

`NdsDsAuthentication.ComputeProgramsHmac` accepts exactly the first 0x160 header bytes, the complete ARM9 authentication representation, and the declared ARM7 bytes. Finalize the common-header CRC and program storage first. ARM9's secure area must use its encrypted authentication representation: the primitive does not encrypt or decompress input, infer KEY1 credentials, or silently substitute image bytes for the supplied program. `ComputeBannerHmac` uses the complete version-defined banner, including stored CRCs and all animated data for version 0x0103, but excludes external alignment padding. Finalize banner edits and CRCs before calling it. The banner key is a separate credential from the program/overlay key. Synthetic-key corpus tests establish byte-processing compatibility, not validation of the original publisher's stored digests.

To request late-DS authenticity checks through `image.Validate`, set `NdsValidationOptions.ValidateDsAuthentication` or supply credentials with `SetDsProgramHmacKey`, `SetDsBannerHmacKey`, or `SetDsRsaPublicKey`. Each credential remains separate. Program verification also needs `SetSecureAreaKeyTable`: the supported canonical layout starts ARM9 at 0x4000 and includes a complete 16 KiB secure area. Verification recognizes encrypted storage or reconstructs the encrypted representation from decrypted storage without modifying source bytes or repairing embedded program fields. A stored-byte CRC match alone does not establish that KEY1 credentials are correct. Short programs, noncanonical layouts, unrecognized encrypted identifiers, and missing credentials produce explicit unverified warnings instead of a false match. Digest and RSA mismatches produce errors. Inspect the `NDS15xx` findings: `IsValid` only means no errors were found, not that every authenticity check had sufficient trust material. Default keyless structural validation does not request these additional checks.

The library does not emulate software, interpret instructions, edit save files, or decode game-specific archives and compression. An image can also contain publisher-specific data that NdsForge preserves as opaque regions rather than claiming to understand it.

## Post-used Download Play signatures

`NdsImage.DownloadPlaySignature` exposes the distinct 0x88-byte trailer conventionally stored at `Header.UsedImageSize`: a four-byte identifier, an opaque 128-byte RSA field, and a four-byte seed. `DownloadPlaySignatureRegion` identifies its physical extent. Detection reads only that bounded location; it never scans capacity padding for a matching byte sequence. An exact identifier with an incomplete payload produces `NDS1551`, and semantic writes reject that truncated state. An explicitly unverified no-op editor copy can still retain every source byte.

Structural imports retain the trailer in `NdsImageBuilder.DownloadPlaySignature`. Builds place it at the new meaningful-image boundary without counting its bytes as meaningful content; DSi components, where present, follow it without overlap. Preservation edits also relocate it when their used-image boundary grows. Setting the builder property to null explicitly omits the stored trailer. Ordinary no-op saves remain byte-exact, including the trailer and subsequent padding.

Semantic builds and edits that retain a trailer return `NDS1550`: the bytes were preserved without revalidation, and changes to the signed header or programs may make them stale. The CLI prints these save diagnostics. This is separate from both the ARM9 per-overlay records and the late-DS/DSi header signature at 0xF80. NdsForge does not generate or cryptographically verify this trailer; parsing, copying, and a structurally valid output are not claims that Download Play hardware will accept its signature.

## Self-contained preservation workspaces

`NdsImageWorkspace.ExportAsync` writes a new workspace directory containing the
versioned `NdsWorkspaceRecipe`, independent native assets, and a complete source
snapshot. The snapshot retains padding, gaps, unknown data, aliased allocations,
carrier material, and any original diagnostics. The recipe records full-image and
component SHA-256 identities, metadata, original regions, File IDs, directory
paths, and overlay relationships. Raw FNT/FAT/overlay tables preserve native
ordering independently from the inventory's sorted semantic view. ARM9 assets
include an adjacent SDK footer when detected. All FAT entries are exported,
including unnamed and zero-length allocations. Numeric host asset names avoid
mapping ROM filenames directly onto host filesystem restrictions.

```csharp
using NdsImage image = await NdsImage.OpenAsync("source.nds");
NdsWorkspaceRecipe recipe = await NdsImageWorkspace.ExportAsync(image, "workspace");
await NdsImageWorkspace.PackFileAsync("workspace", "identical.nds");
```

`ReadRecipeAsync` validates only the bounded UTF-8 JSON description. Exact packing
also independently derives metadata and component regions from the snapshot,
checks every input's size and SHA-256, and verifies the complete temporary output
before atomic publication. It rejects changed inputs rather than silently using
the snapshot in place of edits. This is distinct from structural workspace import.
No repair, resigning, or
source-validity claim accompanies a byte-exact pack.

Recipes require schema version 1 and reject unknown fields, duplicate JSON
properties, numeric enums, traversal, rooted paths, ambiguous file/directory
collisions, and nonportable filenames. Serialized recipes are limited to 32 MiB;
supported snapshots fit the 4 GiB image address space. Paths may be renamed in the
recipe provided that each input still matches its original identity. Inputs and
outputs reject detected symbolic links and reparse-point ancestors. Keep the
workspace stable during an operation: this is not a sandbox against an adversary
concurrently replacing filesystem objects. Export requires a new directory;
packing requires output outside the workspace and explicit overwrite authority
from the caller, never from recipe data. Both use temporary siblings and leave
existing destinations untouched when an operation fails before publication.

Workspaces contain original payload bytes and duplicate the complete physical
image alongside individual components; they are not payload-free inventory
manifests and must not be committed or shared without appropriate rights.

## Cartridge and digital-SRL carriers

`NdsImage.SizeInfo` distinguishes physical input length, common NTR used size,
optional DSi total used size, and nominal device capacity. `DeclaredContentEnd`
includes declared programs, tables, allocations, protocol windows, authentication
coverage, used-size declarations, and recognized Download Play trailers. It is not
found by scanning for the last nonzero or non-FF byte. `PostUsedData` can contain
DSi programs and trailers; `TrailingData` names the range beyond declared content
without asserting that those bytes are padding or disposable. When late-DS
authentication coverage cannot be established, the extent conservatively retains
the complete input and reports a warning. Missing declared content is an error.

The size snapshot retains the raw device-capacity exponent and exposes a nullable
64-bit byte count. Exponents above 45 are unrepresentable and explicitly diagnosed;
the older non-nullable `Header.DeviceCapacityBytes` property throws
`InvalidOperationException` for those values instead of overflowing or wrapping.
Inventory manifests retain the raw exponent and use zero for an unrepresentable
non-nullable capacity byte count; inspection diagnostics explain the invalid value.
This inspection API does not itself resize or discard any bytes.

### Structural capacity and padding

`NdsImageBuildOptions.RequestedDeviceCapacityBytes` selects a header capacity,
not an output length. It accepts exact powers of two from 128 KiB through 4 GiB;
null selects the smallest capacity containing the complete structural layout.
Requests smaller than the layout fail before stream truncation, including when
DSi-only regions, an opaque trailer, or authentication coverage cause the excess.
Individual content offsets and used-size fields still have their 32-bit limits.

`PadToDeviceCapacity = true` additionally extends a cartridge image to that
capacity. The added tail uses `PaddingByte` (default `0xFF`), including when a
compatibility profile uses another byte for interior gaps. Padding never changes
the common used-size field, the DSi total-used field, or component positions.
Header checksums and explicitly requested authentication are generated after the
capacity byte is finalized. A requested larger header capacity with padding
disabled remains a compact file. Both options default to the previous compact
structural-build behavior.

```csharp
var options = new NdsImageBuildOptions
{
    RequestedDeviceCapacityBytes = 32L * 1024 * 1024,
    PadToDeviceCapacity = true,
    PaddingByte = 0xFF,
};
await builder.WriteAsync("full-capacity.nds", options);
```

Digital SRL builds reject these explicit cartridge-only requests; their imported
capacity byte is informational and remains preserved independently from file
length. Contiguous `MemoryStream`/`BuildAsync` outputs larger than the runtime's
array limit are rejected before allocation; use a file/seekable stream for large
outputs. File writes retain the atomic path-overwrite contract. Arbitrary streams
can still fail during I/O or allocation and are not transactional.

These options construct a new layout. They do not preserve an input image's
unknown trailing bytes and are not an in-place trim operation. Use the resizing
API below or a no-op preservation save when retained bytes must remain exact.

### Source-preserving physical resizing

`NdsImageResizer.WriteAsync(image, stream, options)` and
`WriteFileAsync(image, path, options)` resize without moving components or editing
any header byte. `NdsImageResizeOptions.Mode` selects one of four contracts:

| Mode | Output length | Preserved bytes |
| --- | --- | --- |
| `Preserve` (default) | Physical input length | Every byte, including unknown trailing data |
| `Trim` | `SizeInfo.DeclaredContentEnd` | All declared content, including DSi programs, digest coverage, and recognized trailers |
| `PadToDeviceCapacity` | Existing header capacity | Every source byte, followed by `PaddingByte` |
| `ExactLength` | Required `OutputLengthBytes` | Every byte before the selected boundary; any expansion uses `PaddingByte` |

An exact length cannot remove declared content or exceed a cartridge's unchanged
header capacity. Capacity padding is expansion-only: it rejects an input already
larger than capacity. Digital SRLs support preservation, trimming, and explicit
physical lengths, but reject cartridge-capacity expansion. Explicit lengths and
capacity expansion are bounded to 4 GiB; contiguous-memory limits still apply.

Shrinking defaults to `TrailingDataPolicy.RequirePadding`: the complete removed
interval must equal `PaddingByte` (default `0xFF`) before the destination changes.
All-FF file contents are retained because trimming uses declared boundaries, not
a backward search for the last non-FF byte. `TrailingDataPolicy.Discard` explicitly
permits dropping unclassified trailing bytes and returns warning `NDS1580` with
the affected range. It never permits deleting known declared components.

Verification validates the source before writing, compares the entire retained
prefix and all added padding afterward, and reparses the result. No new keys or
authentication generation are needed: covered bytes remain exact. This does not
turn unverified stored signatures into trusted signatures. If late-DS coverage
cannot be resolved, its existing warning conservatively retains the full input.
Missing declared content and ambiguous/malformed carrier layouts cannot be
repaired by padding and are rejected even with verification disabled. An explicit
`Preserve` operation with `VerifyOutput = false` can still copy a damaged image.

Streams must have independent storage, including through wrappers. Direct use of
the source stream as destination is rejected. Path writes reject detected
reparse-point destinations and ancestors, use a temporary sibling, and require
`OverwriteDestination` to replace an existing regular file. They do not provide a
filesystem-wide defense against hostile concurrent path replacement. Keep the
source immutable and use an output directory you control.

`NdsImage.CarrierLayout` distinguishes `NdsCartridgeLayout`, `NdsDigitalSrlLayout`, and `NdsUnknownCarrierLayout`. Storage carrier and `Header.Kind` are separate: the latter describes execution mode. Detection uses executable title categories and declared layout, never the filename extension. An absent title category retains ordinary cartridge/homebrew conventions. Unsupported categories and digital titles with nonzero cartridge access boundaries produce explicit errors rather than an inferred digital layout.

`PostHeaderData` retains independently reserved bytes at 0x1000–0x3FFF for either carrier. It is never read beyond physical EOF, nor treated as opaque reservation when it overlaps a declared program, table, banner, or allocation. Digital titles require the complete reservation; truncation, absence, and overlap produce `NDS1561`. Contradictory access boundaries produce `NDS1560`; unsupported or non-executable title categories produce `NDS1562`. Semantic writes reject malformed/unresolved carrier layouts before destination mutation. An explicitly unverified no-op preservation save can still copy the original bytes exactly, retaining existing validation findings.

Structural imports retain the carrier, opaque bytes, and digital capacity metadata. New recipes select `NdsImageBuilder.Carrier` explicitly and provide a matching title category in `DsiMetadata`; `SetPostHeaderData` copies caller bytes. Digital builds preserve the opaque reservation, keep cartridge access-boundary words zero, align ARM9i to 1024 bytes and other sections to the requested section alignment, and do not expand to nominal cartridge capacity. Imported digital capacity bytes are retained even when their nominal value is smaller than the physical output. The ordinary CRC field is recalculated from raw bytes when a complete interval starts with ARM9 at 0x4000; digital images do not acquire a KEY1 secure area merely because program data occupies that address. `NdsSecureArea.Inspect(image)` therefore reports `Absent` for digital titles; the isolated-buffer crypto APIs still require callers to provide appropriate cartridge context.

Explicit empty ARM9i/ARM7i definitions are supported for digital builds when their matching load/entry addresses are nonzero. Stored title IDs, other program payloads, and opaque material survive rebuilds, but structural writing does not promise whole-image byte identity or publisher-signature validity. DSi authentication remains governed by the recipe's existing explicit integrity policy. DS-mode digital system headers can be inspected and copied, but their structural writer is currently rejected as independently unverified. Explicit trim/expansion policies and portable workspace serialization are separate capabilities, not implied by carrier detection.

DSi cartridge builds place optional digest tables after their covered NTR content, include those tables in the common used-size field, and place the TWL access boundary on a 512 KiB boundary after common content and any retained Download Play trailer. ARM9i starts 12 KiB after that boundary; ARM7i starts beyond both ARM9i's actual bytes and its minimum 16 KiB secure window. The total DSi size includes this cartridge-only layout. These are file-layout guarantees, not a claim of hardware bootability or repaired publisher authentication.

`NdsCartridgeLayout.TwlReservedData` and `TwlReservedRegion` expose the bounded 12 KiB reservation preceding ARM9i. Imports preserve it exactly even when structural rebuilding relocates the boundary. `NdsImageBuilder.SetTwlReservedData` accepts exactly 12 KiB of explicit opaque bytes; an empty value selects deterministic generation from three copies of final image bytes `[0x8000,0x9000)`. This generation is the default for new recipes, not for imported reservations. Preservation saves reject payload writes that would overwrite the reservation. Contradictory boundaries, overlapping/truncated reservations, and programs inside protocol-only intervals produce errors; absent boundary declarations in older DSi homebrew produce an explicit warning without guessing their hardware layout.

## Structural workspace import

`NdsImageWorkspace.ImportAsync` validates the complete original snapshot,
inventory, component roles, and source-region identities, then reads supported
payload edits into a detached `NdsImageBuilder`. Every input must exist, including
unchanged assets; safe-path and link checks apply to them all. The builder remains
usable after the workspace is moved or removed.

```csharp
NdsImageBuilder builder = await NdsImageWorkspace.ImportAsync("workspace");
builder.Title = "EDITED";
builder.FileSystem.AddFile("/new.txt", "new content"u8);
if (builder.DsMetadata is not null)
    builder.DsMetadata.Integrity = NdsDsIntegrityOptions.PreserveStored;
await builder.WriteAsync("rebuilt.nds", new NdsImageBuildOptions
{
    RequestedDeviceCapacityBytes = 128 * 1024 * 1024,
    OverwriteDestination = false,
});
```

Supported asset edits are stored ARM9/ARM7/ARM9i/ARM7i programs, named/private
overlay allocations, other named file allocations, banners, debug executables,
carrier reservations, and retained Download Play trailers. An existing ARM9
footer must remain the valid final twelve bytes of its asset and is kept separate
from the program's declared length. Uncompressed overlay replacement updates RAM
size; compressed replacement must decode safely and retain the original RAM
size. Use the returned builder's explicit recompression operation to change that
size. Carrier reservations retain their original byte lengths; fixed-format
banners and trailers must remain parseable.

Header, FNT, FAT, overlay-table, and digest-table assets are immutable baseline
inputs in this import profile. Editing them is an error, not an ignored change.
Use the returned builder's typed metadata and filesystem operations instead.
All original allocation roles must remain present; unreferenced unnamed
allocations and multiple overlays sharing an unnamed allocation are explicitly
rejected because the structural builder does not yet represent those relationships.
Exact `pack` still preserves these layouts.

Structural builds assign new offsets and File IDs, preserve supported semantic
relationships, and do not promise arbitrary gaps, unmodeled physical aliases,
capacity padding, or byte-exact output. Late-DS authentication requires a caller
policy when declared. DSi imports use the builder's unsigned default; choose the
desired integrity and digest-generation options explicitly before building.
Preserving stored authentication does not make it valid after a structural edit.
The existing missing-authentication-record protections remain in force.

`NdsWorkspaceImportOptions` defaults to 256 MiB per original/edited asset and
1 GiB for the sum of each role's larger original or edited length. Compressed
overlay decoding is also bounded by the per-asset ceiling. These are input
materialization limits, not peak process-memory promises; detached builders and
edited copies can coexist. The full preservation snapshot is streamed separately.
The CLI `build` command exposes verified deterministic reconstruction with explicit
capacity, padding, and DS/DSi integrity policies. Typed metadata and filesystem
structure edits remain library operations. See the command-line reference.

## NAND cartridge partition boundaries

`NdsHeader.NandRomEndUnits` and `NandWritableStartUnits` retain the independent
16-bit fields at 0x94 and 0x96. Their `NandRomEndOffset` and
`NandWritableStartOffset` projections use 128 KiB units for DS and 512 KiB for DSi,
with 64-bit arithmetic even for declarations larger than supported output sizes.
Zero means unspecified, not proof that a cartridge lacks NAND. These addresses
do not describe the writable partition's length, a captured save, the file's used
size, or its required physical length.

Structural imports preserve both raw values. The builder exposes the same raw
properties for explicit metadata changes; it does not relocate save partitions.
The smallest automatic device capacity must contain both nonzero boundaries as
well as all planned content. Compact output remains compact; physical expansion
still requires `PadToDeviceCapacity`. A requested capacity below either boundary,
content crossing either known boundary, overlapping ROM/writable declarations,
non-cartridge declarations, or boundaries beyond the 4 GiB output limit fail
before stream mutation, including when verification is disabled. Different
non-overlapping boundaries and a single unspecified boundary remain representable.
These are conservative structural-write constraints, not hardware boot guarantees.
Preservation edits also reject file, banner, or retained-trailer writes beyond a
known boundary before destination mutation, even with verification disabled;
ending exactly at a boundary is valid. Unchanged whole-image copies remain lossless.

Inspection warns about capacity conflicts (`NDS1590`), content crossing a boundary
(`NDS1591`), overlapping declarations (`NDS1592`), a single unspecified boundary
(`NDS1593`), or a non-cartridge declaration (`NDS1594`). Raw values remain readable.
Neither `SizeInfo.DeclaredContentEnd` nor source-preserving trim infers file data
from partition addresses; removed bytes still require the chosen trailing-data
policy. Manifests and workspace inventories include both nullable raw projections.
Older inventories with both projections absent remain valid: exact packing still
checks their complete image and raw-header hashes. A partially absent pair or a
present but incorrect value is rejected.

## Reading untrusted images

Treat image offsets, lengths, counts, names, and tables as attacker-controlled. Parsing uses checked arithmetic and `NdsReadOptions` limits before allocating table- or hierarchy-sized collections. Applications accepting uploads should select limits appropriate to their service rather than relying only on generous library defaults.

Extraction applies portable host-name rules and rejects traversal, collisions, and reparse-point redirection. It cannot make extracted executable or game content safe to run. Keep unknown material in an isolated workspace and apply the same malware and content policies used for other binary uploads.

## Integrity and authenticity

CRCs, hashes, digest trees, HMACs, and RSA signatures answer different questions. A matching checksum establishes consistency with the stored checksum; it does not establish who produced the image. Authenticity validation requires a public key or secret material supplied by the caller where the format requires it.

Cartridge header RSA signatures cover the first 0xE00 bytes and use a 1024-bit type-one padded raw SHA-1 digest, without the ASN.1 `DigestInfo` wrapper used by ordinary SHA-1 RSA signature APIs. Verification checks the complete encoded block, including its padding. `NdsDsiRsaSignatureProvider` supports that native encoding with randomized input and exponent blinding and checks each result with the public exponent before publishing it. Its managed arbitrary-precision arithmetic does not guarantee constant-time execution or erasure of every temporary mathematical value. For a signing service or hostile shared-host environment, supply an isolated native or hardware implementation of `INdsDsiSignatureProvider` instead. Signing with a caller's key does not confer trust from a console or publisher.

NdsForge contains no boot ROM data, KEY1 tables, retail trust keys, private keys, certificates, or proprietary logo data. A structural import may temporarily retain key bytes already embedded in the caller's ARM9 program solely to maintain its own verified Download Play records. Key-bearing APIs copy caller input into private buffers and do not include it in manifests or diagnostic messages. Callers remain responsible for secure key storage, access control, and disposal of their own copies.

## ndstool interoperability

NdsForge is not a line-for-line port of ndstool. The `Ndstool1503` build profile and differential tests reproduce behavior only where a deterministic, valid oracle exists. The default builder intentionally uses NdsForge's documented layout and safety choices.

Compatibility expectations are tied to complete-file SHA-256 values. Contributors can supply the corresponding legally dumped images locally; ROM bytes, extracted payloads, original paths, and console logs are not committed. See [corpus testing](corpus-testing.md) for the evidence model and known divergences.

## Safe writes

Path-based preservation saves and builds write a temporary sibling and only replace the requested destination after successful completion. Stream overloads cannot provide that transactional boundary and may leave partial bytes after cancellation or I/O failure. Use a path overload when an existing artifact must remain intact.

Preservation edits preflight payload, banner, and retained-trailer writes against other declared components before truncating a destination stream. The common used-size boundary is not necessarily free space: DSi programs and digest tables may follow it. If an append or aliased allocation would overlap another program, table, banner, debug region, or allocation, the save fails with an explicit overlap error even when output verification is disabled. Use a structural builder to relocate such components together. This safety check does not regenerate DSi authentication after edits and does not change the byte-exact no-op copy contract.

Keep backups when editing irreplaceable dumps. Validate output, inspect the returned plan or build result, and compare hashes or manifests before deleting an original image.
