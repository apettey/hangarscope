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

Automatic on every push to `master` (`.github/workflows/release.yml`). The versioning code is **shared** with
eve-contracts and multi-box and lives in `apettey/eve-contracts` (composite actions `next-version`,
`finalize-release` and `validate-features`, called `@main`; the code is `scripts/versioning/` there, with its tests).
This repo has no copy; its differences are action inputs: `features-path: docs/whats-new/features.json`,
`changelog-dir: docs/changelog`, `app-name: HangarScope`, `notes-title: "{app} {tag}"` and
`release-log-notes: full`.

1. Merge the PR with a merge commit.
2. The `version` job runs the shared `next-version` action. It finds the newest **published** `vX.Y.Z` tag in
   `master`'s history (its GitHub Release is not a draft and has the Velopack `*-full.nupkg`) and decides once per
   merge since then (first-parent commits): that merge's PR labels, its changed paths, and `features.json` before
   and after it (only an entry with a **new** `pr` counts as a feature). The biggest of those bumps wins, so one
   PR's `semver:patch` can't downgrade another PR's feature, and `prerelease` is `true` if any of those PRs has
   `deploy:manual`. A failed PR label lookup fails the run; labels are never treated as absent. The release notes
   start with `# HangarScope vX.Y.Z`, then the new what's-new entries, the `docs/changelog/*/README.md` entries added
   since that tag (full text, headings nested, relative links pinned to the commit), every PR in the release, and
   the compare link.
3. The `build` job calls the shared `apettey/eve-contracts/.github/workflows/build-windows-app.yml` with
   `version`, `release-tag: vX.Y.Z` and `prerelease`: `dotnet test HangarScope.Tests`,
   `dotnet publish -p:Version=X.Y.Z`, zip, Velopack pack (`Setup.exe` + full/delta packages). **Only after all of
   that succeeds** it creates the `vX.Y.Z` tag on the built commit and publishes the GitHub Release with the zip and
   the Velopack files (a prerelease when `prerelease` is true). If publishing fails before a Release is out, it
   deletes the tag it created and any draft, so the next run releases those changes again. It refuses to publish a
   version below one that is already released.
4. The `finalize` job runs the shared `finalize-release` action: it replaces the Release's generated notes with the
   ones from step 2, fails if the Release is a draft or lacks the zip or the `*-full.nupkg`, and warns if the
   Release is a prerelease without `deploy:manual`, or was already a normal release when a run built it despite
   `deploy:manual` (a promoted release is normal and gets no warning).
5. Check the run is green and the release has the zip and Velopack packages. Installed copies auto-update from the
   latest non-prerelease release. That is the deploy.

Releases run one at a time (`concurrency: release`). If merges queue up, GitHub drops the middle pending runs, but
the next run still covers them: it decides per merge since the newest published tag, the biggest bump wins and the
notes list every entry and PR.

Re-runs and manual runs:

- **Re-run failed jobs** is safe: the shared workflow reuses a tag that already points at this commit (and refuses
  one on another commit), doesn't upload Velopack packages again once the Release has a `*-full.nupkg`, and
  re-uploads the zip with `--clobber`. A failed `finalize` re-runs on its own. Re-running an old failed build after
  a newer version has shipped is guarded by the shared build: it fails the stale run without tagging or publishing
  anything and tells you to start a fresh release run (Actions → Release → Run workflow on `master`, or
  `gh workflow run release.yml --ref master`); that run releases everything since the newest published tag.
- A run on a commit that is already tagged reuses the tag: if its Release isn't published yet it builds and
  publishes it; if it is, only `finalize` runs (notes rewritten, state checked). The labels' prerelease decision
  only applies when the run creates the Release; an existing one keeps its state.
- A run (or re-run of all jobs) on a commit that is already in a later tag does nothing.
- `finalize` fails if the Release is still a draft or lacks the zip or the `*-full.nupkg`. A draft: publish it with `gh
  release edit vX.Y.Z --draft=false` if it has every file, otherwise delete the draft and run the workflow by hand on
  `master`. A published Release without the zip has shipped; attach the zip by hand with `gh release upload vX.Y.Z
  <zip>`. Take it from the build run's artifacts (kept 7 days). After that, rebuild it from the tag: `git checkout
  vX.Y.Z`, then `dotnet publish HangarScope/HangarScope.csproj -c Release -r win-x64 --self-contained true
  -p:Version=X.Y.Z -o publish`, and zip the folder's contents as `HangarScope-X.Y.Z-win-x64.zip`. Until the zip is
  attached, every run's `finalize` on that commit fails.
- `workflow_dispatch` only releases from `master`; on any other branch it does nothing. It is also the manual
  fallback if a run needs repeating: there is no separate release script any more.

`<Version>` in `HangarScope.csproj` is only the local-build default; the tags are the version of record.

PR CI (`.github/workflows/ci.yml`) validates `docs/whats-new/features.json` with the shared `validate-features`
action (valid JSON, required fields, unique positive `pr`, real dates, plain-text descriptions, 1-2 images whose
`src` starts with `img/` and exists under `docs/whats-new`, entries added by the PR use its number), predicts the
version with the shared `next-version` action in `pr` mode (the PR on top of any unreleased merges on `master`) and
writes **"Merging this PR would release vX.Y.Z"** to the job summary (re-run on label changes), then builds, runs
`dotnet test HangarScope.slnx` and runs the win-x64 release publish.

Changing the versioning rules or code is a PR in `apettey/eve-contracts` (it changes all three apps' releases);
dry-run it against this repo from there before merging.

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
