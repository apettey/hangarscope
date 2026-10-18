# Versioning and release

## Every change is a release

SemVer `MAJOR.MINOR.PATCH`, one version per merged PR:

1. **A label wins:** `semver:major`, `semver:minor`, `semver:patch`. If several are set, the biggest wins.
2. Otherwise a PR that adds a `docs/whats-new/features.json` entry (a user-visible feature) is **minor**.
3. **Everything else is patch:** fixes, refactors, CI, dependency bumps (Dependabot PRs included), docs, process
   changes and direct pushes to `master`.
4. **Major is never automatic.** Breaking changes (settings or cache formats that can't be read anymore, removed
   features) need `semver:major`.

There is no skip. Every merge to `master` is in a released version, every version has release-log entries.

| Label | Meaning |
| --- | --- |
| `semver:major` | Breaking change |
| `semver:minor` | New feature (even without a what's-new entry) |
| `semver:patch` | Force patch (e.g. a what's-new entry for a small tweak) |
| `deploy:manual` | Publish as a prerelease that installed copies ignore until it is promoted (section below) |

## How a release happens

Automatic on every push to `master` (`.github/workflows/release.yml`):

1. Merge the PR with a merge commit.
2. The `version` job runs the release script's tests, then `scripts/release_version.py bump` once per merge since
   the latest `v*` tag (first-parent commits): that merge's PR labels, its changed paths, and `features.json` before
   and after it (only an entry with a **new** `pr` counts as a feature). `release_version.py combine` takes the
   biggest of those bumps, so one PR's `semver:patch` can't downgrade another PR's feature, and prints `bump`,
   `version`, `reason` and `prerelease` (`true` if any of those PRs has `deploy:manual`). A failed PR label lookup
   fails the run; labels are never treated as absent. `release_version.py notes` builds the release notes from the
   `docs/changelog/*/README.md` entries added since the tag (headings nested, relative links pinned to the
   commit), with new what's-new entries on top; with no entry (e.g. Dependabot) it lists the commit subjects.
3. The `build` job calls the shared `apettey/eve-contracts/.github/workflows/build-windows-app.yml` with
   `version`, `release-tag: vX.Y.Z` and `prerelease`: `dotnet test HangarScope.Tests`,
   `dotnet publish -p:Version=X.Y.Z`, zip, Velopack pack (`Setup.exe` + full/delta packages). **Only after all of
   that succeeds** it creates the `vX.Y.Z` tag on the built commit and publishes the GitHub Release with the zip and
   the Velopack files (a prerelease when `prerelease` is true). If publishing fails before a Release is out, it
   deletes the tag it created and any draft, so the next run releases those changes again.
4. The `finalize` job replaces the Release's generated notes with the ones from step 2
   (`gh release edit vX.Y.Z --notes-file`).
5. Check the run is green and the release has the zip and Velopack packages. Installed copies auto-update from the
   latest non-prerelease release. That is the deploy.

Releases run one at a time (`concurrency: release`). If merges queue up, GitHub drops the middle pending runs, but
the next run still covers them: it decides per merge since the latest tag, the biggest bump wins and the notes list
every entry.

Re-runs and manual runs:

- **Re-run failed jobs** is always safe: the shared workflow reuses a tag that already points at this commit (and
  refuses one on another commit) and doesn't upload Velopack packages again once the Release has a `*-full.nupkg`;
  the zip is re-uploaded with `--clobber`. A failed `finalize` re-runs on its own.
- A push whose commit is already tagged without a complete release (published, not a draft, with the zip) is
  skipped: the tag's own run (`release.ps1`) or an earlier run owns it. If one is left like that, run the workflow by
  hand on `master` (Actions → Release → Run workflow): on a tagged commit whose release isn't complete it finishes
  that version on the same commit, with the same `deploy:manual` prerelease decision. Push runs never do this, so
  they can't race a `release.ps1` tag build.
- A run on a tagged commit whose release is complete only rewrites the notes (`finalize`).
- `workflow_dispatch` only releases from `master`; on any other branch or a tag it does nothing.

`<Version>` in `HangarScope.csproj` is only the local-build default; the latest tag is the version of record.

PR CI (`.github/workflows/ci.yml`) runs the same script tests, validates `docs/whats-new/features.json` (valid JSON,
required fields, unique positive `pr`, real dates, image files exist), predicts the version and writes **"Merging
this PR would release vX.Y.Z"** to the job summary (re-run on label changes), then builds, runs
`dotnet test HangarScope.slnx`, checks `release.ps1` finds the test projects, and runs the win-x64 release publish.

### Manual fallback

Only if the workflow can't run: on an up-to-date `master`, `.\scripts\release.ps1 <patch|minor|major>`. It tests
every `*.Tests.csproj`, bumps from the higher of `<Version>` and the latest tag, commits `Release vX.Y.Z`, tags
and pushes; the tag push builds and publishes the old way (tag first, then build). Use the bump the rules above
give.

### `deploy:manual`

Use it when installed apps must not pick up the release until something outside the artefact is done, for example a
new ESI scope that has to be added to the EVE developer application first. The release is still versioned, built,
tagged and published on merge, but as a **prerelease** (`vpk upload github --pre`, `gh release --prerelease`).
Installed apps read only stable releases (`GithubSource(..., prerelease: false)` in `Services/UpdateChecker.cs`), so
they don't update to it; new users can still download it from the Release page. Once the outside work is done,
promote it:

```
gh release edit vX.Y.Z --prerelease=false
```

Installed apps pick it up on their next check (30s after launch, then every 6 hours). If any PR in a release has the
label, the whole release is a prerelease. Merging more PRs before promoting releases them as newer versions on top;
each one that has no `deploy:manual` itself is a normal release, which installed apps **do** update to, and it
includes the held change. So don't merge anything else until the held release is promoted, or label those PRs
`deploy:manual` too. PR CI's job summary says when a merge would publish a prerelease.

### Rollback

Release the previous good code as a new patch version (revert the merge and merge the revert: that releases).
Velopack only moves forward, so don't delete the bad release's tag.

## Known gaps (automate later)

- Optional: fail a PR that adds no folder under `docs/changelog/`.
