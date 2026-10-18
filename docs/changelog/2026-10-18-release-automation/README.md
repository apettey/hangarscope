# Release automation

**Date:** 2026-10-18

## Description

Every merge to `master` is now released by CI without a manual step: the version is worked out from the merged PRs,
the app is tested, built and packed, and only then is the tag created and the GitHub Release published. PR CI shows
which version a merge would release. A `deploy:manual` label now holds a release for real: it is published as a
prerelease that installed copies ignore until it is promoted with `gh release edit vX.Y.Z --prerelease=false`.

## Change

- New `scripts/release_version.py` (standard-library Python): `bump` (biggest `semver:*` label, else a new
  `docs/whats-new/features.json` entry = minor, else patch; major only by label; `prerelease=true` with
  `deploy:manual`), `combine` (one version for merges that queued up: the biggest of their per-merge bumps, a
  prerelease if any of them has `deploy:manual`), `latest-tag`, `notes` (release
  notes from new `docs/changelog` entries and what's-new entries) and `validate-features`. Tests:
  `scripts/tests/test_release_version.py` (`python3 -m unittest discover -s scripts/tests`), run in both workflows.
- `.github/workflows/release.yml`: on push to `master` computes the version, the prerelease flag and the notes
  (a failed PR label lookup fails the run), then calls the shared `apettey/eve-contracts` `build-windows-app.yml`
  with its existing `version`, `release-tag` and `prerelease` inputs (it runs `HangarScope.Tests`, publishes, packs,
  then tags and releases, and removes the tag again if publishing fails before a Release is out). A `finalize` job
  then sets the release notes with `gh release edit --notes-file`. Re-running failed jobs is safe; a tag left without
  a complete release is finished by running the workflow by hand on `master` (manual runs only release from
  `master`). `v*` tag pushes still build the old way. No change to the shared workflow is needed.
- Labels `semver:major`, `semver:minor`, `semver:patch` and `deploy:manual` created in `apettey/hangarscope`.
- `.github/workflows/ci.yml`: validates `features.json`, runs the script tests, writes "Merging this PR would release
  vX.Y.Z" to the job summary, runs `dotnet test HangarScope.slnx` and the win-x64 release publish, and checks that
  `release.ps1` finds the test projects.
- `scripts/release.ps1` (now a manual fallback): finds `*.Tests.csproj` projects (it missed `HangarScope.Tests`),
  fails if it finds none, bumps from the higher of `<Version>` and the latest tag, and has `-ListTests`.
- `AGENTS.md`, the `ship-change` skill, the PR template and `README.md` no longer ask for a manual release step.
- No app code, settings, ESI endpoints or scopes changed. `Services/UpdateChecker.cs` already reads stable releases
  only (`GithubSource(..., prerelease: false)`), which is what makes the `deploy:manual` prerelease a hold. Not run
  locally: PowerShell isn't installed on the machine this was written on, so `release.ps1` was verified in CI only;
  `dotnet build` and `dotnet test HangarScope.slnx` (40 passed) ran locally on macOS with `-p:EnableWindowsTargeting=true`.
