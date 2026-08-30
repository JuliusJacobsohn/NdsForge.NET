# Formats, compatibility, and safety {#formats_and_safety}

## Supported image families

NdsForge reads and models original Nintendo DS images, DSi-enhanced images, and DSi-exclusive images. Support covers interpreted headers, programs, overlays, NitroFS tables and payloads, banner families, secure-area state, and the DSi regions represented by the public API. Late-generation DS images that opt into DSi-era banner or program authentication retain their complete 0x1000-byte header and expose the declared HMAC and signature fields separately from the DSi unit model.

Common headers expose the optional debug executable as an offset, byte length, load address, and bounded region. Structural builders represent it as an owned `NdsDebugProgramDefinition`, assign its physical location with the other components, and verify both metadata and bytes after rebuilding. Manifests hash the executable separately from the header. A header with only one of offset or length, or with a region outside the physical image, receives diagnostic `NDS1109`.

Territory, launch, crypto, access-control, application-policy, memory-bank, shared-data, EULA, and parental-rating fields have typed projections alongside their complete native values. Typed builder setters change only their assigned bits where a field contains unassigned bits; raw properties and preserved header templates retain everything else. `NdsDsiAgeRating` keeps the complete authority byte while separately exposing age, applicability, prohibited/pending state, and the unassigned bit. Manifests include the corresponding raw scalars and canonical lowercase hexadecimal strings for MBK, shared-data-size, and rating arrays, so semantic comparison detects these edits without embedding payload data.

Canonical ARM9 SDK parameter tables are recognized only when both byte-order markers are present and the complete fixed prefix lies inside the program. This prevents a legacy tool-generated footer placeholder or an arbitrary in-range pointer from being reported as SDK metadata. Stored authentication bytes are evidence from the image, not proof of authenticity; verification still requires caller-selected trust material.

Classic-DS overlays whose control record enables Download Play authentication are linked to the positional 20-byte records inside decoded ARM9. `NdsImage.Arm9OverlayAuthentication` reports whether the SDK footer pointer is complete, missing, or outside the decoded program, as well as plain-versus-BLZ storage and the retained prefix. Validation accepts an explicit key through `SetArm9OverlayHmacKey`; without one, it considers a conventional 64-byte ARM9 key block usable only after that candidate reproduces every flagged record. A marked block is never trusted merely because its prefix looks familiar.

Structural imports retain verified table and key state privately. `NdsImageBuilder.ReplaceOverlay` can preserve stored bytes, store decoded bytes directly, or apply deterministic BLZ encoding. Before layout assigns offsets, the builder updates the stored-size and compression fields, hashes the exact stored overlay payloads, patches decoded ARM9, re-encodes ARM9 when required, and repairs its SDK compressed-end address. A missing key, stale source table, bad footer pointer, unrepresentable BLZ output, or compressed-end field outside the verbatim prefix fails before output is written. The local preservation editor deliberately rejects authenticated ARM9 overlay replacement because it cannot relocate a newly compressed ARM9 atomically; use a structural builder for that operation.

The library does not emulate software, interpret instructions, edit save files, or decode game-specific archives and compression. An image can also contain publisher-specific data that NdsForge preserves as opaque regions rather than claiming to understand it.

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

Keep backups when editing irreplaceable dumps. Validate output, inspect the returned plan or build result, and compare hashes or manifests before deleting an original image.
