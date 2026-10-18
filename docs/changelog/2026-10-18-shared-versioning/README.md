# Shared release versioning

**Date:** 2026-10-18

## Description

The release versioning now runs on the shared implementation in `apettey/eve-contracts`, the same code eve-contracts
and multi-box use, instead of this repo's own `scripts/release_version.py`. The rules are unchanged; the copy, its
tests and the old `release.ps1` fallback are gone.

**Depends on apettey/eve-contracts#20** (the shared actions); until it is merged, the actions this repo calls
`@main` don't exist.

## Change

- `.github/workflows/release.yml`: the `version` job is the shared `next-version` action (inputs:
  `docs/whats-new/features.json`, `docs/changelog`, `HangarScope`, notes title `{app} {tag}`, full release-log text
  in the notes); `finalize` is the shared `finalize-release` action (notes, draft/zip/package check, prerelease
  warnings). The build is unchanged.
- Kept: per-merge bumps with the biggest winning, `deploy:manual` = prerelease, a failed PR lookup fails the run,
  only tags in `master`'s history count, a commit already in a later release does nothing.
- Changed:
  - Only *published* tags (a Release with the Velopack packages) are a base; a tag left by a failed run is stepped
    over.
  - A tagged commit whose Release isn't published is built again on any run, not only a manual one (no tag-push
    build is left to race it).
  - A published Release without the zip is not rebuilt; `finalize` says to attach the zip by hand.
  - The notes also list every PR and the compare link. The "No release-log entry. Commits:" fallback is replaced by
    that PR list.
  - The PR preview counts unreleased merges already on `master`.
  - The features check also rejects unknown fields, HTML in descriptions and more than two images.
- `.github/workflows/ci.yml`: the features check and the version preview use the shared actions; the release-script
  tests and the "release.ps1 finds the test projects" step are gone.
- Removed: `scripts/release_version.py`, `scripts/tests/`, `scripts/release.ps1` and the `v*` tag-push build
  (`build-tag`). The manual way to repeat a release run is `gh workflow run release.yml --ref master`.
- Docs: AGENTS.md rule 5, the ship-change skill (repo values, release step, ordering traps,
  `references/versioning-and-release.md`). Re-running an old failed build after a newer version shipped is now
  documented as guarded by the shared build.
- Tested:
  - The shared suite in eve-contracts (150 tests) and `actionlint`.
  - A dry run against this repo's real history matches the old decisions: `v1.2.1` at `master` is published, so
    notes only, and older commits are skipped. With the `v1.2.1` tag removed in a scratch clone, the dry run gives
    `v1.2.1` (patch, #15 and #16).
  - Not run: a release with the new workflow on GitHub.
- ESI endpoints or scopes: none.
