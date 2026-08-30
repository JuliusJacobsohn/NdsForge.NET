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
