# Releasing

Every completed meaningful feature receives a version increment, semantic commits,
and a prompt push. Releases use one stable Semantic Version from `Version.props`
for `NdsForge`, `NdsForge.Nitro`, `NdsForge.Graphics`, `NdsForge.Audio.Wav`, and
`NdsForge.Cli`. Use a minor increment for additive public features, a patch for
compatible fixes, and a major increment for intentional breaking changes.

## Prepare and merge

1. Complete focused synthetic, malformed-input, and applicable private-corpus
   verification. Keep private images, reference tools, and research artifacts out
   of Git and hosted-runner uploads.
2. Increment `Version.props`, add dated release notes and a comparison link to
   `CHANGELOG.md`, and update affected documentation. CI rejects package-source
   changes without a version increment, including automated dependency updates.
3. Promote delivered entries from each library's `PublicAPI.Unshipped.txt` into
   `PublicAPI.Shipped.txt`, retaining the nullable header in both. Update each
   existing library's `PackageValidationBaselineVersion` to its latest published
   version; new packages have no prior baseline. Do not suppress accidental
   compatibility breaks. The first `1.1.0` release checks the core against `1.0.1`.
4. Run `./build/build.ps1`. To retain earlier local evidence, use a distinct
   `-ArtifactSubdirectory release-candidate` (a simple directory name under
   `artifacts`). The gate audits locked dependencies, formatting, analyzers,
   release policy, source size, tests, coverage, all packages, isolated consumers,
   and strict documentation. It resets only its generated test/coverage/package
   directories; do not put research evidence in those output directories.
5. Run the complete private compatibility suite when preparing a feature release.
   The manual private-image workflow requires a trusted self-hosted Windows runner
   with `NDSFORGE_CORPUS`, `NDSFORGE_DIGITAL_CORPUS`, `NDSFORGE_TEST_ROM`,
   `NDSFORGE_NDSTOOL`, and `NDSFORGE_NDSTOOL_SOURCE`. The last variable identifies
   the required file, not a directory. No skipped tests count as a passing gate.
6. Push semantic commits and open a pull request. Merge only after Ubuntu and
   Windows CI and the required private checks pass. Private fixtures are not
   available on public hosted runners, so the local/private gate remains an
   explicit maintainer responsibility.

## Automatic publication

Successful `main` CI triggers `release.yml`. The release checks out the exact
successful CI commit, confirms main ancestry, reruns the complete release gate,
and creates the immutable `v<version>` tag. It authenticates through NuGet Trusted
Publishing, publishes all five packages and their symbols, waits for public NuGet
downloads, and tests those downloaded packages in isolated library/CLI consumers.
Only then does it create the stable GitHub release with changelog notes, package
assets, and SHA-256 checksums. Documentation deployment also runs on main.
Hosted CI and documentation jobs have a 10-minute limit; publication has a
20-minute limit, including up to 10 minutes for NuGet indexing. The full private
ROM matrix runs locally or on the explicitly selected self-hosted runner, never
on GitHub-hosted runners.

Pull requests, fork runs, failed CI, and unmerged branches cannot trigger
publication. The privileged workflow does not consume artifacts from the
triggering workflow. Publishing uses a short-lived token; no NuGet API key is
stored in GitHub. The NuGet policy must authorize this repository's `release.yml`
workflow and all five package IDs, including creation of the companion packages.

A documentation-only merge with an already completed version is a no-op for
publishing. A package change with the same version fails. NuGet versions are
immutable; never replace a tag or attempt to overwrite a published version.

## Dry runs and recovery

The Release workflow retains manual dispatch. Run it from `main` with `release`
disabled to prepare and inspect an unpublished candidate without tags or uploads.
Enable `release` only to publish or recover a candidate with successful main CI.

If a job fails after tagging or after publishing some packages, rerun that job.
Alternatively, dispatch from `main` with `commit` set to the exact tagged SHA and
`release` enabled. The same-commit retry skips existing NuGet versions and repairs
the missing release steps. It must never rebuild that version from a newer SHA.
An unrelated tag, an incomplete older release, or packages without a matching tag
fail closed and require investigation. A NuGet indexing timeout can be retried
without a new version. A fully published version and GitHub release need no retry.
