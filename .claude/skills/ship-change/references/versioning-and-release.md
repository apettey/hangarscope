# Versioning and release

## Every change is a release

SemVer `MAJOR.MINOR.PATCH`, one version per merged PR:

1. **A label wins:** `semver:major`, `semver:minor`, `semver:patch`. If several are set, the biggest wins.
2. Otherwise a PR that adds a `docs/whats-new/features.json` entry (a user-visible feature) is **minor**.
3. **Everything else is patch:** fixes, refactors, CI, dependency bumps (Dependabot PRs included), docs, process
   changes and direct pushes to `master`.
4. **Major is never automatic.** Breaking changes (settings or cache formats that can't be read anymore, removed
   features) need `semver:major`.

There is no skip. Every commit on `master` maps to a version, every version to a release-log entry.

| Label | Meaning |
| --- | --- |
| `semver:major` | Breaking change |
| `semver:minor` | New feature (even without a what's-new entry) |
| `semver:patch` | Force patch (e.g. a typo fix in `features.json`) |
| `deploy:manual` | The release must wait for work outside the artefact (section below) |

## How a release happens today

Release automation is partly manual in this repo. Until it's automated, do the missing steps by hand on every merge.
Never drop the rule.

1. Merge the PR with a merge commit.
2. `git switch master && git pull --ff-only`
3. `dotnet test HangarScope.slnx` — `release.ps1`'s own test step only finds projects under a folder named
   `test`/`tests`, so it skips `HangarScope.Tests`, and the tag build passes no `test-project` either.
4. `.\scripts\release.ps1 <patch|minor|major>` (PowerShell). It checks the tree is clean and on `master`, bumps
   `<Version>` in `HangarScope/HangarScope.csproj`, commits `Release vX.Y.Z`, tags `vX.Y.Z` and pushes.
   `-Watch` follows the CI run.
5. The tag triggers `.github/workflows/release.yml`, which calls the shared
   `apettey/eve-contracts/.github/workflows/build-windows-app.yml`: `dotnet publish` with `-p:Version=X.Y.Z`, zip,
   Velopack pack (`Setup.exe` + full/delta packages) and upload to the GitHub Release for the tag.
6. Check the run is green and the release has the zip and Velopack files. Installed copies auto-update from the
   latest release. That is the deploy.

Release each merge before merging the next PR, so every merge gets its own version. If two merges do land
between releases (e.g. Dependabot), release once per merge commit in order, or, if that's no longer possible, release
once with the biggest bump and say in the release notes which entries it covers.

### `deploy:manual`

Use it when installed apps must not pick up the release until something outside the artefact is done, for example a
new ESI scope that has to be added to the EVE developer application first. There is no way to hold a release once
it's tagged: the shared workflow runs `vpk upload github --publish`, and installed apps check for updates 30s after
launch and every 6 hours (`Services/UpdateChecker.cs`). So do the outside work **before** running `release.ps1`
(ideally before merging), and don't merge anything else until that release is out. Nothing reads the label yet; it
tells the person releasing to wait.

### Rollback

Release the previous good code as a new patch version (revert the merge, merge the revert, release). Velopack only
moves forward, so don't delete the bad release's tag.

## Known gaps (automate later)

The target state, from the shared way of working:

- A release workflow on every push to `master` that works out the bump from the merged PR (labels, then
  `features.json` diff, else patch), builds and publishes the artefact, and **only then** creates the tag and
  release with notes from the release log. Today the tag comes first and triggers the build, so a failed build
  leaves a tag without packages: fix forward with a new patch release.
- A testable bump script (input: changed paths, latest tag, PR labels; output: `bump`, `version`, `reason`) with
  tests in CI.
- PR CI that validates `docs/whats-new/features.json` (valid JSON, required fields, unique `pr`), runs the release
  publish, and posts "Merging this PR would release vX.Y.Z" in the job summary.
- `release.ps1` doesn't find `HangarScope.Tests` (its filter needs a `test`/`tests` folder) and the tag build runs
  no tests: run `dotnet test HangarScope.slnx` by hand before releasing until one of them is fixed.
- A real `deploy:manual` hold (publish the release as a draft, publish it later).
- Optional: fail a PR that adds no folder under `docs/changelog/`.

## CI today

- `ci.yml` (every PR): `dotnet build -c Release`, then `dotnet test` for every `*.Tests.csproj`.
- `release.yml` (push to `master`, `v*` tags): Windows build via the shared workflow; artefact zip on every run,
  GitHub Release + Velopack packages on tags.
