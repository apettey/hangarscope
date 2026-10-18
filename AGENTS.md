# AGENTS.md — working on HANGARSCOPE

Guidance for AI agents (and humans) contributing to this repository.

## Project facts

- **Stack: .NET 10 + Avalonia UI** (C#, XAML, MVVM via CommunityToolkit.Mvvm). Do **not** introduce Electron, web frontends, or other UI stacks — this was an explicit project decision.
- Build: `dotnet build HangarScope\HangarScope.csproj` · Run: `dotnet run --project HangarScope`
- The design contract is `design/README.md` + `design/Asset Ledger.dc.html`. It is high-fidelity: colors, typography (Barlow Condensed / IBM Plex Mono, letter-spaced labels), spacing, and copy are final intent. No border radius, no shadows, 1px borders only.
- App data (settings, DPAPI-encrypted credentials/tokens, caches, `sync.log`) lives in `%APPDATA%\HangarScope`.
- ESI credentials are entered by the user in the SETTINGS screen only. Never hardcode, log, or commit credentials or tokens.
- Real characters can own thousands of asset stacks. Any list bound to asset-scale data must be virtualized (see the ListBox in `AssetsView.axaml`), and per-item aggregations in `MainViewModel.Recompute()` must be precomputed into dictionaries, not nested LINQ scans.

## Shipping changes (required for every change)

Follow the `ship-change` skill ([`.claude/skills/ship-change/SKILL.md`](.claude/skills/ship-change/SKILL.md)) for every change, of any kind. This file wins if the two disagree; update the skill in the same PR that changes a rule. Six rules are blocking — a change missing any of them is not done:

1. **Tests are in place.** Every change adds or updates tests for the behaviour it changes; `dotnet test HangarScope.slnx` passes locally and in CI.
2. **Review loop.** A fresh reviewer (a subagent or person who didn't write the change) reviews the diff; fix every finding and re-review until a round has zero findings. The round count goes in the PR body.
3. **Before/after screenshots** for every visible change: `before.png` taken before the first edit, `after.png` of the final version, stored next to the release-log entry and shown in the PR.
4. **Release-log entry** in the same PR: `docs/changelog/YYYY-MM-DD-<short-name>/README.md` from `docs/changelog/TEMPLATE.md`, with a row at the top of `docs/changelog/README.md`. Visible changes also get a user-facing entry in `docs/whats-new/features.json` keyed by the PR number.
5. **Every merge gets a new version** — SemVer, no skip: a what's-new entry means minor, everything else patch, `semver:*` labels override, major only by label. `.github/workflows/release.yml` does this on every push to `master` (bump from `scripts/release_version.py`, build and pack, then tag and GitHub Release); PR CI shows the version a merge would release. Merges that queue up behind a running release are released together in the next version, with the biggest of their bumps. `scripts/release.ps1` is only a manual fallback.
6. **Release quickly.** Small PRs, merged the same day, released straight after merge; installed apps auto-update from the GitHub Release (Velopack).

Feature documentation still lives alongside the log: when a feature is added or a screen changes, update `README.md` / `docs/FEATURES.md` (what it does, where it lives in the UI, new settings, files or ESI scopes) and refresh `docs/screenshots/` (PNG, kebab-case names, e.g. `assets-filter-rail.png`).
Capture from the demo dataset: `HangarScope.exe --demo --screen=<tab> --snapshot=<path>.png` renders the window to a PNG and exits (`--refit-mode=fittings`, `--refit-scroll=orders|lists` `--fit-sync=<fit name part> [--fit-sync-pick=all]`, `--fit-tidy=<fit name part>` and `--fit-expand=all` reach the other REFIT views). Check SETTINGS captures before committing: that screen shows the local machine's real ESI client ID even in demo mode.

## Verification expectations

- `dotnet build` must succeed with 0 warnings/errors before committing.
- `dotnet test HangarScope.slnx` must pass. Planner/format logic (e.g. `Services/RefitPlanner.cs`, `Services/EftFormat.cs`) stays pure so it can be covered in `HangarScope.Tests`; add tests with every rule change.
- Anything that writes to the game (saving/deleting in-game fittings) must be an explicit button followed by an in-UI confirmation step. Fit sync goes through the per-pilot picker (`FitSync` in `Services/FitSync.cs`): save the new copy before deleting an old one, and only delete after the save succeeded. Fit merging, variant grouping, search ranking and the tidy plan live in the pure `Services/FitCatalog.cs`; the picker UI (`Views/FitPickerView.axaml`, `ViewModels/FitPickerVm.cs`) is shared by the IN-GAME FIT ▾ flyout and the FITTINGS library.
- Launch the app and visually verify affected screens (the app shows a demo dataset when no character is linked, so every screen is exercisable without credentials).
- Real-data paths (SSO, sync) log to `%APPDATA%\HangarScope\sync.log` — check it when debugging sync issues.
