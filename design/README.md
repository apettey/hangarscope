# Handoff: HANGARSCOPE — EVE Online Multi-Character Asset Tracker

## Overview
A self-hosted Windows desktop application (jEveAssets-style) that aggregates assets across multiple EVE Online characters via ESI, tracks each character's live location, computes jump distance from characters to assets, values everything against Jita market prices, and provides a wallet overview. Authentication is EVE SSO (OAuth2) with user-supplied ESI application credentials.

## About the Design Files
The file in this bundle (`Asset Ledger.dc.html`) is a **design reference created in HTML** — a prototype showing intended look and behavior, not production code. The task is to **recreate this design in the target environment**. No codebase exists yet; recommended stack for a self-hosted Windows desktop app: **Tauri or Electron + React/TypeScript**, or .NET/WPF if preferred. The prototype's logic (filtering, sorting, aggregation) is readable JavaScript inside the file and maps directly to real implementation logic.

## Fidelity
**High-fidelity.** Colors, typography, spacing, and copy are final intent. Recreate pixel-perfectly. All mock data (characters, stations, assets, journal entries) must be replaced with live ESI data.

## App Chrome (Windows)
- Custom title bar, 32px tall, background #080a0d, bottom border 1px #141a21. Left: app title, 12px, letter-spacing 1.5px, color #6b7887. Right: minimize / maximize / close buttons, each 46×32px, hover background #141a21 (close hover: #c0533f with white glyph). Wire to real window controls.
- Below it a 56px header bar (#0d1015, bottom border 1px #1e2630): logo mark (26px square, 1px #4fc3f7 border), brand "HANGARSCOPE" (19px/700/3px tracking) + subtitle, nav tabs, Jita SELL/BUY price-side toggle, and total net worth (all characters) in cyan mono.
- Nav tabs: ASSETS · DASHBOARD · WALLET · LOCATIONS · CHARACTERS · SETTINGS. 13px, letter-spacing 2px; active tab has 2px #4fc3f7 bottom border + #eef4f8 text; inactive #6b7887.

## Screens

### 1. ASSETS (main asset browser)
Two-column grid: 264px filter rail + fluid table.

**Filter rail** (#0d1015, right border 1px #1e2630, 16px padding, 18px gaps):
- Search input (filters item name, group, station text).
- CHARACTERS: multi-select chip list — "All characters" + one per character, each with 8px square color dot, name, and per-character total value in mono. Selected: border #3a4a5c, bg #131a22.
- DISTANCE FROM: select — "Closest character" (min jumps across selected characters) or a specific character.
- 2×2 grid of selects: REGION, CATEGORY (Ships/Modules/Minerals/Ammunition/Drones/Blueprints/Commodities/Implants), OWNERSHIP (All/Hangar/Fitted/In container), MIN VALUE (Any/>1M/>10M/>100M/>1B ISK).
- LOCATIONS tree: regions (uppercase, with total value) → stations (indented, 2px left border, stack count). Clicking a station toggles it as a filter (active: cyan border/text, bg #131a22).
- CLEAR FILTERS button resets everything.

**Table**: 8 columns, grid-template-columns 2.4fr 0.7fr 1fr 1fr 1.8fr 0.7fr 1fr 1.1fr:
ITEM (with FITTED amber / CTNR gray tag chips) · QTY · GROUP · OWNER (color dot + name) · LOCATION · JUMPS · UNIT · VALUE.
- Rows 40px, bottom border 1px #141a21, hover bg #10151c. Numeric cells: IBM Plex Mono 12px, right-aligned. Text cells truncate with ellipsis.
- Sortable headers: ITEM, QTY, JUMPS, VALUE (▲/▼ indicator, click toggles direction).
- JUMPS color: 0 = #8bd450, ≤10 = #cfd8e0, >10 = #c0533f. Computed per the DISTANCE FROM setting.
- Footer bar: "{n} STACKS · FILTERED VALUE {x} ISK" + price-sync status line.

### 2. DASHBOARD (net worth)
- Row of 4 cards: TOTAL NET WORTH (26px cyan mono) + one card per character (value, stack count, top holding).
- VALUE BY REGION: horizontal bars (6px tall, #4fc3f7 at 0.85 opacity on #141a21 track), width proportional to value.
- TOP HOLDINGS: ranked table (rank, item, owner dot+name, location, value), top 8 by value.

### 3. WALLET
- Cards row: TOTAL LIQUID ISK (+ combined net worth incl. assets) + per-character balance cards with 30-day delta (+green/−red).
- BREAKDOWN chip row: All characters / per character — filters everything below.
- 30-DAY FLOW BY TYPE: signed bars per aggregated ref_type (BOUNTY PRIZES, MARKET SELLS, MARKET BUYS, FEES + TAX, CONTRACTS, ESCROW RELEASED, INSURANCE, TRANSFERS). Income #8bd450, expense #c0533f. NET 30 D total below.
- 30-DAY FLOW BY STATION: table with IN / OUT / NET columns per station, sorted by |net|, plus an "IN SPACE / NO STATION" line for station-less entries (bounties, insurance, transfers).
- RECENT JOURNAL: date, owner, description, station, ref_type, signed amount. Header notes ESI's 30-day journal window and 1h cache.

### 4. LOCATIONS (character tracker)
3-up card grid, one per character:
- Header: 44px initials avatar (1px border in character color), name, status line (DOCKED · … / IN SPACE · ON GRID).
- 2×2 facts: SYSTEM (with security status colored: ≥0.5 green, >0 amber, ≤0 red), REGION, SHIP, CHARACTER VALUE.
- NEAREST ASSET HUBS: stations holding assets sorted by jumps from this character — "HERE" in green when 0 jumps, else "{n} J".

### 5. CHARACTERS (SSO / auth)
- LINK A CHARACTER panel: explainer copy + "AUTHENTICATE VIA EVE SSO →" outline button (cyan; hover inverts to solid cyan/dark text). Launches OAuth2 PKCE flow.
- Linked characters table: character (dot+name), scopes granted, token expiry countdown, last sync, SYNC / UNLINK actions (UNLINK hover turns #c0533f).
- Footer: required scopes — esi-assets.read_assets.v1, esi-location.read_location.v1, esi-location.read_ship_type.v1, esi-universe.read_structures.v1 (add esi-wallet.read_character_wallet.v1 for the wallet screen).

### 6. SETTINGS
- ESI APPLICATION · SELF-HOSTED (full-width panel): user-supplied CLIENT ID field, SECRET KEY masked input with SHOW toggle, read-only CALLBACK URL (http://localhost:PORT/sso/callback), TEST CONNECTION and SAVE CREDENTIALS buttons, "CREDENTIALS VERIFIED" green status dot. Copy states credentials are stored encrypted locally and never leave the machine.
- MARKET DATA panel: price hub (Jita 4-4/Amarr/Dodixie/Rens), default price side (sell/buy/split), price refresh interval.
- SYNC panel: asset sync interval (ESI cache is 1h), location tracking cadence (live 30s / 5min / off), citadel name resolution policy, cache size + PURGE CACHE.

## Interactions & Behavior
- All filters combine (AND). Character chips are additive multi-select; empty selection = all.
- Jump distance: per-asset station → route length from character's current system (use ESI /route/ or a static graph of system gates; recompute when a character moves).
- Price side toggle re-values everything instantly (assets carry both sell and buy unit prices).
- Location tree and character chips show live counts/values that respect the other active filters (except station selection itself).
- Hover states throughout: rows tint #10151c, buttons/chips lighten border to #3a4a5c, cyan CTAs invert.
- No animations required; the aesthetic is instant/industrial.

## State Management
Per prototype state: active screen, search text, selected character set, price side (sell/buy), distance-from mode, region/category/ownership/min-value filters, selected station, sort key + direction, wallet character filter.
Data stores needed: characters (id, name, portrait, current system/ship/docked status), stations/structures (name, system, region), asset stacks (type, group, category, qty, owner, location, flag), market prices (typeId → sell/buy at chosen hub), wallet balances + journal (accumulate locally beyond ESI's 30-day window).

## ESI Integration
- Auth: EVE SSO OAuth2 (authorization code + PKCE), user-supplied client ID/secret, refresh tokens per character.
- Endpoints: /characters/{id}/assets/ (paginated), /characters/{id}/location/, /characters/{id}/ship/, /universe/structures/{id}/, /universe/names/, /markets/{region_id}/orders/ (The Forge for Jita prices), /characters/{id}/wallet/, /wallet/journal/ (v6), /wallet/transactions/, /route/{origin}/{destination}/.
- Respect ESI cache timers (assets 1h, location ~5s, journal 1h) and ETag headers.

## Design Tokens
Colors: bg #0b0d10 · panel #0d1015 · title bar #080a0d · rules #1e2630 · faint rules #141a21 · hover row #10151c / #131a22 · text #cfd8e0 · bright text #e4ebf1/#eef4f8 · muted #8fa0b0 · dim #6b7887 · accent cyan #4fc3f7 · green #8bd450 · amber #ffb300 · red #c0533f · olive (scopes ok) #7c9a5e.
Typography: Barlow Condensed (400–700) for UI text; IBM Plex Mono (400–600) for all numerals, ISK values, timestamps, IDs. Labels: 11px, letter-spacing 2px, uppercase, #6b7887. Body 14–15px. No border radius anywhere — everything square. No shadows; 1px borders only.
ISK formatting: abbreviate (2.60 B / 812.0 M / 41.7 K); sub-10 values get 2 decimals.
Scrollbars: thin, thumb #1e2630 on #0b0d10 track.

## Assets
None — no images or icon fonts. Logo mark is pure CSS (bordered square + inner squares). Character portraits are initials avatars in the prototype; production can use EVE's character portrait endpoint (images.evetech.net).

## Files
- `Asset Ledger.dc.html` — the full prototype: all six screens, all mock data, and working filter/sort/aggregation logic in the embedded `Component` class.
