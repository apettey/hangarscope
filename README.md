# HANGARSCOPE — New Eden Asset Ledger

A self-hosted Windows desktop application (jEveAssets-style) that aggregates assets across multiple EVE Online characters via ESI, tracks each character's live location, computes jump distances from characters to assets, values everything against Jita market prices, and provides a wallet overview.

Built with **.NET 10 + Avalonia UI** (C#, XAML, MVVM). The UI is a faithful recreation of the high-fidelity design prototype in [`design/`](design/), extended to scale to accounts with many characters.

![Assets browser](docs/screenshots/assets.png)

## Screens

Full feature documentation with screenshots: **[docs/FEATURES.md](docs/FEATURES.md)**.

- **ASSETS** — filterable/sortable asset browser: search, multi-select character chips (value-sorted, scrollable), distance-from selector, region/category/ownership/min-value filters, location tree, virtualized 8-column table with jump-distance coloring.
- **DASHBOARD** — total net worth, per-character cards in a value-sorted wrap grid, value-by-region bars (top 12 + OTHERS), top 10 holdings.
- **WALLET** — liquid ISK totals, per-character balances with 30-day delta, 30-day flow by ref-type and by station, accumulated journal (kept locally beyond ESI's 30-day window).
- **LOCATIONS** — live per-character cards: system (security-colored), region, ship, docked status, nearest asset hubs by jumps.
- **CHARACTERS** — EVE SSO (OAuth2 + PKCE) linking, token expiry, per-character sync/unlink.
- **SETTINGS** — user-supplied ESI application credentials (stored DPAPI-encrypted, never leave the machine), price hub/side, sync cadences, citadel name policy, cache purge.

Until a character is linked, the app shows the design's demo dataset so every screen is explorable (`--demo` forces it).

## Getting started

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download).
2. `dotnet run --project HangarScope`
3. Register an application at [developers.eveonline.com](https://developers.eveonline.com) with callback URL `http://localhost:8635/sso/callback` and these scopes:
   - `esi-assets.read_assets.v1`
   - `esi-location.read_location.v1`
   - `esi-location.read_ship_type.v1`
   - `esi-universe.read_structures.v1`
   - `esi-wallet.read_character_wallet.v1`
4. In **SETTINGS**, paste your client ID + secret key, **SAVE CREDENTIALS**, then **TEST CONNECTION**.
5. In **CHARACTERS**, click **AUTHENTICATE VIA EVE SSO** — your browser opens; log in and authorize. Repeat per character.

Assets, prices, locations, wallet and routes then sync on the cadences configured in SETTINGS (respecting ESI cache timers: assets/journal 1 h, location ~5 s).

## Architecture

```
HangarScope/
  Models/        domain types (characters, stations, asset stacks, journal, settings)
  Services/
    EsiClient    ESI HTTP client: ETag caching, X-Pages pagination, error-limit backoff
    SsoService   EVE SSO OAuth2 authorization-code + PKCE, localhost callback listener
    UniverseSvc  type/group/category, station/structure, system/region resolution + cache
    SyncService  orchestrates syncs, aggregates asset stacks, computes routes, builds snapshots
    JsonStore    JSON persistence under %APPDATA%\HangarScope, DPAPI for secrets/tokens
  ViewModels/    MVVM (CommunityToolkit.Mvvm); MainViewModel ports the prototype's logic 1:1
  Views/         six screens + custom title-bar window, styled to the design tokens
  Controls/      FractionBar (proportional value bars)
```

Storage: everything lives in `%APPDATA%\HangarScope` — settings, encrypted credentials/refresh tokens (Windows DPAPI), and a `cache/` directory (assets, prices, routes, universe, accumulated journal) that PURGE CACHE clears. Sync events log to `sync.log`, UI timings to `perf.log`.

Performance: the asset table is virtualized and the view model recomputes per-screen sections lazily (search debounced, sync broadcasts coalesced, sort clicks reorder without re-filtering) — the UI stays responsive with tens of thousands of asset items. For fastest startup, publish with ReadyToRun:

```bash
dotnet publish HangarScope -c Release -r win-x64 --self-contained false -o publish
```

## Fonts

Bundled [Barlow Condensed](https://fonts.google.com/specimen/Barlow+Condensed) and [IBM Plex Mono](https://fonts.google.com/specimen/IBM+Plex+Mono), both under the SIL Open Font License (see `HangarScope/Assets/Fonts/`).

## License

MIT. EVE Online and all related assets are property of CCP hf. This is a third-party tool using the public ESI API.
