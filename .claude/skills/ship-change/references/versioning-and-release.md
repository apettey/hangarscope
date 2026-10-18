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
| `deploy:manual` | The release must wait for work outside the artefact (section below) |

## How a release happens

Automatic on every push to `master` (`.github/workflows/release.yml`):

1. Merge the PR with a merge commit.
2. The `version` job runs the release script's tests, then `scripts/release_version.py bump` once per merge since
   the latest `v*` tag (first-parent commits): that merge's PR labels, its changed paths, and `features.json` before
   and after it (only an entry with a **new** `pr` counts as a feature). `release_version.py combine` takes the
   biggest of those bumps, so one PR's `semver:patch` can't downgrade another PR's feature, and prints `bump`,
   `version` and `reason`. `release_version.py notes` builds the release notes from the
   `docs/changelog/*/README.md` entries added since the tag (headings nested, relative links pinned to the
   commit), with new what's-new entries on top; with no entry (e.g. Dependabot) it lists the commit subjects.
3. The `build` job calls the shared `apettey/eve-contracts/.github/workflows/build-windows-app.yml` with
   `version` and `release-notes`: `dotnet test HangarScope.Tests`, `dotnet publish -p:Version=X.Y.Z`, zip,
   Velopack pack (`Setup.exe` + full/delta packages). **Only after all of that succeeds** it creates the `vX.Y.Z`
   tag on the built commit and publishes the GitHub Release with the zip, the Velopack files and the notes.
4. Check the run is green and the release has the zip and Velopack packages. Installed copies auto-update from the
   latest release. That is the deploy.

Releases run one at a time (`concurrency: release`). If merges queue up, GitHub drops the middle pending runs, but
the next run still covers them: it decides per merge since the latest tag, the biggest bump wins and the notes list
every entry. A push whose commit is already tagged is skipped (the tag's own run, or an earlier run, owns it). If
publishing failed after the tag was pushed, the release is missing, a draft, or lacks the zip: run the workflow by
hand on `master` (Actions → Release → Run workflow). A manual run on a tagged commit whose release isn't complete
finishes that version on the same commit: the shared workflow reuses the tag, `vpk upload --merge`s into the
existing release and publishes a draft. Push runs never do this, so they can't race a `release.ps1` tag build.

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
new ESI scope that has to be added to the EVE developer application first. The release can't be held: merging
releases straight away (`vpk upload github --publish`), and installed apps check for updates 30s after launch and
every 6 hours (`Services/UpdateChecker.cs`). So do the outside work **before merging**, and don't merge anything else
until that release is out. Nothing reads the label yet; it tells the person merging to wait.

### Rollback

Release the previous good code as a new patch version (revert the merge and merge the revert: that releases).
Velopack only moves forward, so don't delete the bad release's tag.

## Known gaps (automate later)

- A real `deploy:manual` hold (publish the release as a draft, publish it later).
- Optional: fail a PR that adds no folder under `docs/changelog/`.
