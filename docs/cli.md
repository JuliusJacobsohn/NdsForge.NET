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
```

`inspect` prints high-level identity and layout information. `validate` emits structured findings in a human-readable form. `list` enumerates NitroFS content, with allocation details in long mode. `extract` applies the same safe host-path rules as the library. `replace` performs a preservation-oriented file edit. `manifest` writes stable JSON, and `diff` reports semantic and physical differences.

## Exit codes

| Code | Meaning |
| ---: | --- |
| `0` | Command completed successfully; compared images are equivalent. |
| `1` | Validation failed or compared images differ. |
| `2` | Command syntax or arguments are invalid. |
| `130` | The operation was canceled. |

The CLI is a thin client over NdsForge. Applications needing filtered extraction, custom validation policy, in-memory I/O, or build composition should use the library API directly.
