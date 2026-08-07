# Security Policy

## Supported versions

Security fixes are provided for the latest stable 1.x release. Users of older builds should upgrade before reporting a defect already fixed in the current package.

## Reporting a vulnerability

Do not open a public issue for a suspected vulnerability. Use GitHub's private **Security → Report a vulnerability** feature. Include the affected NdsForge version and commit, operating system and .NET runtime, expected impact, and the smallest synthetic reproducer you can provide.

Never submit a commercial ROM, private dump, firmware, key, certificate, proprietary logo, or identifying local path. If a safe fixture cannot be attached, describe how a maintainer can construct equivalent synthetic bytes.

## Security boundary

Treat image structures, names, offsets, tables, manifests, ELF inputs, and extracted content as untrusted. NdsForge uses bounded parsing, checked arithmetic, safe extraction paths, validation diagnostics, and transactional path writes to reduce risk. These controls do not make unknown executable or extracted content safe to run.

Cryptographic consistency is not automatically publisher authenticity. Trust-bearing operations require caller-owned inputs, and NdsForge does not distribute Nintendo secrets or certificates. See [formats, compatibility, and safety](docs/formats-and-safety.md) for operational guidance.
