# Release automation

**Date:** 2026-10-18

## Description

Every merge to `master` is now released by CI without a manual step: the version is worked out from the merged PRs,
the app is tested, built and packed, and only then is the tag created and the GitHub Release published. PR CI shows
which version a merge would release.

## Change

- New `scripts/release_version.py` (standard-library Python): `bump` (biggest `semver:*` label, else a new
  `docs/whats-new/features.json` entry = minor, else patch; major only by label), `combine` (one version for merges
  that queued up: the biggest of their per-merge bumps), `latest-tag`, `notes` (release
  notes from new `docs/changelog` entries and what's-new entries) and `validate-features`. Tests:
  `scripts/tests/test_release_version.py` (`python3 -m unittest discover -s scripts/tests`), run in both workflows.
- `.github/workflows/release.yml`: on push to `master` computes the version and notes, then calls the shared
  `apettey/eve-contracts` `build-windows-app.yml` with the new `version` and `release-notes` inputs (which runs
  `HangarScope.Tests`, publishes, packs, then tags and releases). A tag left without a complete release is finished by
  running the workflow by hand on `master`. `v*` tag pushes still build the old way.
- Shared workflow (`apettey/eve-contracts`): new optional `version` and `release-notes` inputs (and reuse of this
  commit's tag, merging into its draft or incomplete release, when an earlier run failed after tagging); callers that don't pass them (eve-contracts, multi-box, eve-k162-tracker) behave as before.
- `.github/workflows/ci.yml`: validates `features.json`, runs the script tests, writes "Merging this PR would release
  vX.Y.Z" to the job summary, runs `dotnet test HangarScope.slnx` and the win-x64 release publish, and checks that
  `release.ps1` finds the test projects.
- `scripts/release.ps1` (now a manual fallback): finds `*.Tests.csproj` projects (it missed `HangarScope.Tests`),
  fails if it finds none, bumps from the higher of `<Version>` and the latest tag, and has `-ListTests`.
- `AGENTS.md`, the `ship-change` skill, the PR template and `README.md` no longer ask for a manual release step.
- No app code, settings, ESI endpoints or scopes changed. Not run locally: .NET SDK and PowerShell aren't installed
  on the machine this was written on, so the build, tests and `release.ps1` were verified in CI only.
