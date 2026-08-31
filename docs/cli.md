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
assets are rejected rather than ignored. The library's `NdsImageWorkspace.ImportAsync`
can import supported payload edits into a structural builder; a CLI `build`
command is not yet exposed. `unpack` and `pack` report existing image validation findings to
standard error without repairing them; exit code zero confirms successful
preservation, not authenticity or hardware acceptance. The snapshot and exported
assets contain original image bytes and require private storage. The snapshot
uses the full physical input size in addition to the exported component storage.

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
capacity is a structural build operation, available through
`NdsImageBuildOptions.RequestedDeviceCapacityBytes`; this command deliberately
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
structural save does not imply signature authenticity. Images declaring late-DS
authentication require an explicit write policy through the library API; the CLI
does not silently preserve, remove, or regenerate those authenticated fields.
