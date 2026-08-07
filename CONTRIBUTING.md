# Contributing

NdsForge.NET is maintained by Julius Jacobsohn and was developed with substantial AI assistance. Disclose substantial generated contributions, and verify them as carefully as handwritten changes.

## Pull requests

Use a focused feature branch and a Conventional Commit title. Keep unrelated refactors separate, preserve existing organization, and update the changelog for user-visible changes. Every behavior change needs focused tests, including malformed input and boundaries where applicable.

The stable 1.x public API is a compatibility contract. Additions require reviewed XML documentation and a public-API baseline update. Removing, renaming, or incompatibly changing a shipped member requires an approved major release.

## Documentation

Documentation should explain Nintendo DS concepts, ownership and lifetime, units, constraints, validation rules, security implications, compatibility boundaries, and important failure modes. Do not use comments that merely restate a signature.

Long-form developer guidance belongs in `docs/`. API details belong beside the relevant declaration. Generated HTML is an artifact and must not be edited directly.

## Test data

Never commit commercial or private ROM data, extracted payloads, keys, certificates, firmware, proprietary logos, original local paths, or console logs containing identifying data. Synthetic images belong in public tests. Real-image expectations must remain hash-bound metadata as described in [docs/corpus-testing.md](docs/corpus-testing.md).

Run the complete release gate before opening a pull request:

```shell
./build/build.ps1
```

Report suspected vulnerabilities privately as described in [SECURITY.md](SECURITY.md). Maintainers should follow [RELEASING.md](RELEASING.md).
