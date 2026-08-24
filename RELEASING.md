# Releasing

Releases are built from `main` and published by the manual Release workflow using NuGet Trusted Publishing. No NuGet API key is stored in GitHub.

1. Add release notes and comparison links to `CHANGELOG.md`.
2. Set the stable Semantic Version in `Version.props`.
3. Commit the release preparation and let cross-platform CI complete.
4. Run the Release workflow with publishing disabled and inspect its packages, symbols, checksums, package-consumer verification, and documentation output.
5. Run the workflow again with publishing enabled. It verifies the version and tag identity, reruns the complete release gate, publishes `NdsForge`, `NdsForge.Nitro`, and `NdsForge.Cli`, and creates the GitHub release.

Publishing is intentionally explicit. Failed jobs can be retried while the version and tag still identify the same commit. If NuGet already contains either package version without the matching release tag, investigate rather than forcing the workflow past its identity checks.
