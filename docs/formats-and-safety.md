# Formats, compatibility, and safety {#formats_and_safety}

## Supported image families

NdsForge reads and models original Nintendo DS images, DSi-enhanced images, and DSi-exclusive images. Support covers interpreted headers, programs, overlays, NitroFS tables and payloads, banner families, secure-area state, and the DSi regions represented by the public API.

The library does not emulate software, interpret instructions, edit save files, or decode game-specific archives and compression. An image can also contain publisher-specific data that NdsForge preserves as opaque regions rather than claiming to understand it.

## Reading untrusted images

Treat image offsets, lengths, counts, names, and tables as attacker-controlled. Parsing uses checked arithmetic and `NdsReadOptions` limits before allocating table- or hierarchy-sized collections. Applications accepting uploads should select limits appropriate to their service rather than relying only on generous library defaults.

Extraction applies portable host-name rules and rejects traversal, collisions, and reparse-point redirection. It cannot make extracted executable or game content safe to run. Keep unknown material in an isolated workspace and apply the same malware and content policies used for other binary uploads.

## Integrity and authenticity

CRCs, hashes, digest trees, HMACs, and RSA signatures answer different questions. A matching checksum establishes consistency with the stored checksum; it does not establish who produced the image. Authenticity validation requires a public key or secret material supplied by the caller where the format requires it.

NdsForge contains no boot ROM data, KEY1 tables, DSi common keys, HMAC keys, private keys, certificates, or proprietary logo data. Key-bearing APIs copy caller input into private buffers and do not include it in manifests or diagnostic messages. Callers remain responsible for secure key storage, access control, and disposal of their own copies.

## ndstool interoperability

NdsForge is not a line-for-line port of ndstool. The `Ndstool1503` build profile and differential tests reproduce behavior only where a deterministic, valid oracle exists. The default builder intentionally uses NdsForge's documented layout and safety choices.

Compatibility expectations are tied to complete-file SHA-256 values. Contributors can supply the corresponding legally dumped images locally; ROM bytes, extracted payloads, original paths, and console logs are not committed. See [corpus testing](corpus-testing.md) for the evidence model and known divergences.

## Safe writes

Path-based preservation saves and builds write a temporary sibling and only replace the requested destination after successful completion. Stream overloads cannot provide that transactional boundary and may leave partial bytes after cancellation or I/O failure. Use a path overload when an existing artifact must remain intact.

Keep backups when editing irreplaceable dumps. Validate output, inspect the returned plan or build result, and compare hashes or manifests before deleting an original image.
