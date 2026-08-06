# Changelog

All notable changes to NdsForge.NET are documented here. The project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
