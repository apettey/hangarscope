---
name: ship-change
description: End-to-end checklist for shipping any change to HangarScope — branch, tests, before/after screenshots, release-log entry, fresh-reviewer loop until a round has no findings, PR with a what's-new entry, a SemVer release on every merge (never skipped) and a fast Velopack auto-update rollout. Use when implementing, testing, screenshotting, reviewing, opening a PR for, or releasing a feature or fix.
---

# Ship a change

`AGENTS.md` is the rules file and wins if it and this skill disagree. Change the skill in the same PR that changes a
rule.

## The six rules (blocking)

A change missing any of these is **not done**.

1. **Tests are in place.** Every change adds or updates tests for the behaviour it changes; the full suite passes
   locally and in CI.
2. **There is a review loop.** A fresh reviewer subagent reviews the diff; fix every finding, re-review, repeat until
   a round has **zero findings**. Round count goes in the PR body.
3. **Features have screenshots.** Visible changes get a `before.png` (taken before the first edit) and an `after.png`
   (final version) next to the release-log entry, and both are shown in the PR.
4. **Every change has a release-log entry** in the same PR. Visible changes also get a what's-new entry.
5. **Every merge gets a new version.** No skip: docs-only and CI-only changes are patch releases.
6. **Release quickly.** Small PRs, merged the same day, released straight after merge.

## Repo values

Everything repo-specific is in this block. To reuse the skill in another repo, change this block and the
repo-specific review rules in [references/review-loop.md](references/review-loop.md).

| Key | Value |
| --- | --- |
| Rules file | `AGENTS.md` |
| Main branch | `master` |
| Branch prefix | `claude/` (humans: `feature/`, `fix/`, `chore/`) |
| Build | `dotnet build HangarScope/HangarScope.csproj` (0 warnings, 0 errors) |
| Run (demo data, no credentials) | `dotnet run --project HangarScope -- --demo` |
| Screenshot | `HangarScope.exe --demo --screen=<tab> --snapshot=<path>.png` (renders the window to PNG at a fixed size and exits; REFIT views: `--refit-mode=fittings`, `--refit-scroll=orders\|lists`, `--fit-sync=<fit name part> [--fit-sync-pick=all]`, `--fit-tidy=<fit name part>`, `--fit-expand=all`) |
| Tests | `HangarScope.Tests/` · `dotnet test HangarScope.slnx` |
| Release artefact | `dotnet publish HangarScope/HangarScope.csproj -c Release -r win-x64 --self-contained true -o publish` (PR CI does **not** run this; the local run is the only check before merge) |
| Release log | `docs/changelog/` (index `README.md`, template `TEMPLATE.md`) |
| What's new | `docs/whats-new/features.json` · images `docs/whats-new/img/` |
| Feature docs | `README.md`, `docs/FEATURES.md`, `docs/screenshots/` (kebab-case PNGs) |
| Version source | `<Version>` in `HangarScope/HangarScope.csproj` |
| Release | `.github/workflows/release.yml` builds on a `v*` tag; `scripts/release.ps1 <patch\|minor\|major>` bumps, tags and pushes (manual until a bump workflow exists) |
| Deploy target | GitHub Release with the Velopack installer + delta packages; installed apps auto-update from it |
| Repo / PR URLs | `apettey/hangarscope` |

The app runs on Windows (DPAPI, Velopack). On a machine without the .NET SDK, say so and don't claim build, test or
screenshot steps passed.

## Lifecycle

Steps marked *(visible)* are skipped for changes users can't see (CI, refactor, docs, process). Steps 4, 6, 7, 8 and 9
are never skipped.

1. **Branch.** `git switch master && git pull --ff-only && git switch -c claude/<short-name>`. One change per branch.
   If other PRs are open, either stack on the newest one, or rebase on the latest `master` just before opening the
   PR and move your rows back to the top of `docs/changelog/README.md` and `docs/whats-new/features.json`.
2. **Before screenshot** *(visible)*. **Before the first edit**, build `master` and capture every screen the change
   will touch into `docs/changelog/YYYY-MM-DD-<short-name>/before.png` (more: `before-<view>.png`). You can't take it
   afterwards.
3. **Build it, with tests.** Keep logic pure in `Services/` (like `RefitPlanner`, `EftFormat`, `FitSync`,
   `FitCatalog`) with thin view-model/view glue. Add tests for the rule changes: empty/unknown data, several
   pilots, ESI error responses. A bug fix starts with a test that fails without the fix. Run the build, the full
   test suite and the release artefact publish.
4. **Try it for real.** Run with `--demo` and exercise the main path, the edge cases and any toggle. If you couldn't
   reach ESI or SSO, say so in the release-log entry.
5. **After screenshot** *(visible)*. Same `--screen` and flags as "before", of the **final** version: `after.png`
   (`after-<view>.png`). Retake it if review fixes change the UI. Also refresh the affected `docs/screenshots/*.png`
   and the `README.md`/`docs/FEATURES.md` sections. Check SETTINGS captures: they show the machine's real ESI client
   ID even in demo mode.
6. **Release-log entry.** Copy `docs/changelog/TEMPLATE.md` to `docs/changelog/YYYY-MM-DD-<short-name>/README.md` and
   add a row at the **top** of the `docs/changelog/README.md` table. Changing an existing feature gets a new entry;
   never rewrite old ones. Formats: [references/formats.md](references/formats.md).
7. **Review loop.** Commit, then run the loop in [references/review-loop.md](references/review-loop.md): spawn a
   fresh reviewer subagent with the diff, the intent and the checklist; fix or answer each finding; send the
   **same** reviewer the new diff; repeat until a round has zero findings. Count the rounds.
8. **Open the PR, then add the what's-new entry.**
   1. Push and open the PR first (body from `.github/pull_request_template.md`) to get its number `N`. Ask the user
      before the first push and the PR.
   2. *(visible)* Add an entry with `"pr": N` at the top of `docs/whats-new/features.json`; save images as
      `docs/whats-new/img/pr-N-<name>.png`. Push, then run one more review round with the same reviewer; it must
      come back clean before merge.
   3. Fill in the PR body: what and why, before/after images linked
      `https://github.com/apettey/hangarscope/blob/<commit-sha>/<path>?raw=true` (pinned to a SHA so they survive
      branch deletion), the release-log link, a test summary, "Review rounds: K, last one clean", and the bump.
   4. Add a `semver:*` label if the default bump is wrong, `deploy:manual` if outside work (e.g. a new ESI scope
      on the developer app) must be done before the release.
   5. CI must be green.
9. **Merge, version, release.** Merge with a merge commit. Then, on an up-to-date `master`, run
   `dotnet test HangarScope.slnx` (the script's own test step misses `HangarScope.Tests`), then
   `.\scripts\release.ps1 <bump>`: minor if the PR added a what's-new entry, otherwise patch, unless a label says
   otherwise. Release each merge before merging the next PR. Check the tag's Release run published the zip and Velopack packages and the GitHub Release exists.
   Details and the gaps still done by hand: [references/versioning-and-release.md](references/versioning-and-release.md).

Copy-paste checklist for the PR or your notes: [references/checklist.md](references/checklist.md).

## Ordering traps

- The "before" screenshot is taken **before the first edit**.
- The PR is opened **before** the what's-new entry, which needs its number.
- With other PRs open, rebase on `master` right before opening the PR and make sure your rows in
  `docs/changelog/README.md` and `features.json` are still at the top.
- The what's-new entry is added after the review loop, so it gets its own review round before merge.
- PR image links are pinned to a **commit SHA**.
- A GitHub Release only counts once the tag build has **published** its artefacts. If the build fails, fix forward
  with a new patch version; don't leave a release without packages.
- `deploy:manual` can't hold a tagged release (it publishes right away and installed apps auto-update): finish the
  outside work before running `release.ps1`.
- Anything that writes to the game (saving or deleting in-game fittings) needs an explicit button plus an in-UI
  confirmation step, and fit sync saves the new copy before deleting the old one.
