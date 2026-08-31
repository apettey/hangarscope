# AGENTS.md — working on HANGARSCOPE

Guidance for AI agents (and humans) contributing to this repository.

## Project facts

- **Stack: .NET 10 + Avalonia UI** (C#, XAML, MVVM via CommunityToolkit.Mvvm). Do **not** introduce Electron, web frontends, or other UI stacks — this was an explicit project decision.
- Build: `dotnet build HangarScope\HangarScope.csproj` · Run: `dotnet run --project HangarScope`
- The design contract is `design/README.md` + `design/Asset Ledger.dc.html`. It is high-fidelity: colors, typography (Barlow Condensed / IBM Plex Mono, letter-spaced labels), spacing, and copy are final intent. No border radius, no shadows, 1px borders only.
- App data (settings, DPAPI-encrypted credentials/tokens, caches, `sync.log`) lives in `%APPDATA%\HangarScope`.
- ESI credentials are entered by the user in the SETTINGS screen only. Never hardcode, log, or commit credentials or tokens.
- Real characters can own thousands of asset stacks. Any list bound to asset-scale data must be virtualized (see the ListBox in `AssetsView.axaml`), and per-item aggregations in `MainViewModel.Recompute()` must be precomputed into dictionaries, not nested LINQ scans.

## Documentation policy (required for every feature)

Every time a new feature is added or an existing feature's behavior/UI changes:

1. **Update the documentation in the same commit** — `README.md` (and `AGENTS.md` if conventions change) must describe the feature: what it does, where it lives in the UI, and any new settings, files, or ESI scopes involved.
2. **Add screenshots showing the feature** — capture the running app with the feature visible and commit them under `docs/screenshots/` (PNG, kebab-case names, e.g. `assets-filter-rail.png`, `wallet-30day-flow.png`). Reference them from the README section that describes the feature. Update stale screenshots when a screen's appearance changes.

A feature PR/commit without updated docs and screenshots is incomplete.

## Verification expectations

- `dotnet build` must succeed with 0 warnings/errors before committing.
- Launch the app and visually verify affected screens (the app shows a demo dataset when no character is linked, so every screen is exercisable without credentials).
- Real-data paths (SSO, sync) log to `%APPDATA%\HangarScope\sync.log` — check it when debugging sync issues.
