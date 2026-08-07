# Core concepts {#core_concepts}

## Images and payloads

`NdsImage` is the immutable root of a parsed image. Headers, programs, overlays, banners, directories, and files describe the source at the time it was opened. Large payloads are represented by bounded regions that can open streams without loading the complete image.

Disposing an image releases its underlying data source. Do not retain or open source-backed payload streams after disposal. Independent memory-backed build inputs are not tied to an image lifetime.

## Parsing and validation

The parser performs the structural and allocation checks required to construct a safe object graph. It deliberately does not reject every checksum or authenticity failure. `NdsImage.Validate` evaluates those semantic and integrity rules and returns all findings in one `NdsValidationResult`.

This distinction lets inspection and recovery tools examine a damaged image while allowing build, deployment, or archival applications to enforce stricter policies.

## Preservation edits

`NdsImageEditor` records changes against an existing image. It prefers in-place allocation reuse and produces a semantic and physical edit plan. Path-based saves use a temporary sibling, verify the result when requested, and replace the destination only after successful completion.

Preservation editing does not pretend that arbitrary file-system restructuring is a local patch. Use a builder when directory structure, file IDs, tables, or general layout must be regenerated.

## Structural builds

`NdsImageBuilder` represents authored content rather than a mutable parsed image. It accepts program definitions, overlays, NitroFS content, banners, DSi metadata, and caller-owned integrity credentials. Identical state and options produce identical bytes.

Build profiles select intentional layout behavior. The ordinary deterministic profile is the default; `Ndstool1503` exists for the subset where the repository has executable-oracle evidence.

## Manifests and comparisons

`NdsImageManifest` is a versioned, stable JSON description suitable for inventories and regression fixtures. It records hashes and interpreted structure, not the underlying payload bytes or secret material.

`NdsImageComparer` complements a byte comparison with semantic differences such as changed identities, files, programs, overlays, or regions. Physical inequality does not necessarily mean semantic inequality, and the API reports both.
