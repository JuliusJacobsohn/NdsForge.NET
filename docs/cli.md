# Command-line reference {#cli_reference}

Install the .NET tool globally:

```shell
dotnet tool install --global NdsForge.Cli
```

Update an existing installation with `dotnet tool update --global NdsForge.Cli`.

## Commands

```text
ndsforge inspect <image.nds>
ndsforge validate <image.nds>
ndsforge list <image.nds> [--long]
ndsforge extract <image.nds> <directory> [--overwrite]
ndsforge unpack <image.nds> <new-workspace-directory>
ndsforge pack <workspace-directory> <output.nds> [--overwrite]
ndsforge build <workspace-directory> <output.nds> [options]
ndsforge replace <image.nds> <nitro-path> <file> <output.nds> [--overwrite]
ndsforge manifest <image.nds> [output.json]
ndsforge diff <left.nds> <right.nds>
ndsforge resize <input.nds> <output.nds> <preserve|trim|pad|exact> [options]
```

`inspect` prints high-level identity and layout information. `validate` emits structured findings in a human-readable form. `list` enumerates NitroFS content, with allocation details in long mode. `extract` applies the same safe host-path rules as the library. `replace` performs a preservation-oriented file edit. `manifest` writes stable JSON, and `diff` reports semantic and physical differences.

Size inspection distinguishes physical length, common used size, optional DSi
total used size, nominal device capacity, and the declared content extent.
Reported trailing bytes are not automatically padding: unknown data must not be
discarded merely because it follows a header's used-size field.
Nonzero NAND partition fields are shown separately with raw units and decoded
addresses. Zero means unspecified; the writable-start address does not report
save-data size or require the input file to extend to that address.

## Portable workspaces and exact packing

`unpack` creates a self-contained, versioned image workspace. Unlike the ordinary
`extract` command's selected components, it includes every FAT allocation and a
complete preservation snapshot, including opaque gaps and trailing bytes. The
destination must be new; merging into or overwriting a workspace is not supported.
Publication occurs only after all assets and `ndsforge-workspace.json` are written.

```shell
ndsforge unpack game.nds game-workspace
ndsforge pack game-workspace identical.nds
ndsforge pack game-workspace identical.nds --overwrite
```

`pack` verifies the original snapshot identity, metadata, region declarations, and
every asset before publishing byte-identical output. Missing files, modified
assets, inconsistent metadata, and detected filesystem links are errors. Output
must be outside the workspace. Existing output requires `--overwrite` and remains
unchanged on verification failure. Paths in the recipe must be portable and
relative to the workspace; the entire folder can be moved to another machine.

This command is an exact-preservation operation, not a structural build: edited
assets are rejected rather than ignored. Use `build` for supported payload edits,
or the library's `NdsImageWorkspace.ImportAsync` for further typed builder edits.
`unpack` and `pack` report existing image validation findings to
standard error without repairing them; exit code zero confirms successful
preservation, not authenticity or hardware acceptance. The snapshot and exported
assets contain original image bytes and require private storage. The snapshot
uses the full physical input size in addition to the exported component storage.

## Structural workspace builds

`build` imports supported edits and writes a deterministic, verified structural
image to a separate path. It can change program, allocation, banner, debug-program,
and retained carrier/trailer payload files. Existing ARM9 footers must remain valid;
compressed overlays must retain their decoded RAM size. Fixed carrier reservations
must retain their length. Header and generated table assets must stay unchanged:
use the library's typed builder operations for metadata edits, file additions,
renames, or changes to overlay runtime/compression policy.

```shell
ndsforge build game-workspace rebuilt.nds
ndsforge build game-workspace padded.nds --capacity 0x8000000 --pad --padding-byte FF
ndsforge build late-ds-workspace rebuilt-ds.nds --ds-integrity preserve
ndsforge build dsi-workspace rebuilt-dsi.nds --dsi-integrity clear
```

`--capacity` selects nominal cartridge capacity, expressed in decimal bytes or
`0x` hexadecimal. It must be a power of two from 128 KiB through 4 GiB and contain
all content and declared NAND boundaries. Without it, the builder selects the
smallest coherent capacity; it does not automatically retain the source capacity.
`--pad` extends physical output to the selected capacity. Without that flag,
output stays compact. `--padding-byte HH` controls layout gaps and final padding
(two hex digits, default `FF`). Digital SRLs reject cartridge capacity and padding
requests. Common/DSi used-size fields exclude final capacity padding.

Late-DS headers declaring authentication require `--ds-integrity preserve` or
`--ds-integrity clear`. Preserve retains stored authentication with warnings that
it can be stale; clear deliberately removes the fields and declaration bits.
All DSi builds require `--dsi-integrity clear` or `--dsi-integrity homebrew`.
Clear removes component authentication and the signature; homebrew generates the
library's public development HMAC/marker identity, not a retail signature. Both
CLI policies omit original hierarchical digest tables. Explicit keys, hierarchical
digest generation, real signing, and overlay-authentication repair credentials
remain library API operations; secrets are never loaded from the workspace recipe.
An inapplicable integrity policy is rejected, not ignored.

Output must be outside the workspace and cannot traverse detected links or use
ambiguous Windows device/alternate-stream names. Existing output requires
`--overwrite`. Missing inputs, failed checks, and cancellation leave existing
output untouched; a temporary sibling is published only after verification.
Verification cannot be disabled through this command. Unsupported orphan/private
allocation relationships are rejected instead of silently discarded.

Structural equivalence does not promise original physical offsets, File IDs,
opaque gaps, trailing data, authentication, or hardware acceptance. For complete
source-byte preservation, use `pack`. Import defaults bound each original/edited
asset to 256 MiB and aggregate native input to 1 GiB (not a peak-memory guarantee);
applications needing different limits should use the library API.

## Physical resizing

`resize` copies to a distinct output path without relocating components or
changing header fields. `preserve` keeps every input byte. `trim` ends after all
declared content, including recognized Download Play trailers, DSi-only content,
and authenticated coverage. `pad` expands to the existing cartridge capacity; it
does not shrink oversized input or apply cartridge rules to digital SRLs.
`exact` requires `--length` in decimal bytes or `0x`-prefixed hexadecimal.

```shell
ndsforge resize game.nds copy.nds preserve
ndsforge resize game.nds trimmed.nds trim
ndsforge resize trimmed.nds expanded.nds pad --padding-byte FF
ndsforge resize game.nds sized.nds exact --length 0x2000000
```

`--padding-byte HH` supplies exactly two hexadecimal digits (default `FF`). Any
removed interval must consist entirely of that byte. To explicitly discard
unclassified trailing data, use `--discard-trailing` with `trim` or `exact`;
the command reports warning `NDS1580`. Known content cannot be discarded by this
flag. Existing output requires `--overwrite`. Duplicate/unknown flags and lengths
on other modes are rejected. Preflight failures leave the destination unchanged.

Exact cartridge length cannot exceed the existing header capacity. Changing that
capacity is a structural build operation, available through `build --capacity` or
`NdsImageBuildOptions.RequestedDeviceCapacityBytes`; `resize` deliberately
does not alter signed header bytes. It validates output and compares every
retained byte and every added padding byte before publishing the result.

## Exit codes

| Code | Meaning |
| ---: | --- |
| `0` | Command completed successfully; compared images are equivalent. |
| `1` | Validation, sizing policy, or I/O failed, or compared images differ. |
| `2` | Command syntax or arguments are invalid. |
| `130` | The operation was canceled. |

The CLI is a thin client over NdsForge. Applications needing filtered extraction, custom validation policy, in-memory I/O, or build composition should use the library API directly.

`replace` prints save warnings to standard error, including retained Download Play
trailers whose signature may become stale after covered data changes. A successful
structural save does not imply signature authenticity. The `replace` command does
not choose a late-DS authentication policy. Use the library API or an explicitly
configured workspace `build` for images requiring that decision.
