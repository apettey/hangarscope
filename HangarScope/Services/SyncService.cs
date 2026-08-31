using System.Text.Json;
using HangarScope.Models;

namespace HangarScope.Services;

/// <summary>Orchestrates ESI syncs (assets, locations, wallet, prices, routes) and builds UI snapshots.</summary>
public sealed class SyncService : IDisposable
{
    private readonly EsiClient _esi = new();
    private readonly JsonStore _store = new();
    private readonly UniverseService _universe;
    private readonly SsoService _sso = new();
    private readonly SemaphoreSlim _syncLock = new(1);

    private AppSettings _settings;
    private List<AuthRecord> _auth;
    private Dictionary<long, CharState> _charState;
    private Dictionary<long, RawStack[]> _assetCache = new();
    private Dictionary<long, PriceEntry> _prices;
    private Dictionary<string, int> _routes;
    private Dictionary<string, JournalEntry> _journal;
    private DateTimeOffset? _priceSyncedAt;

    private System.Timers.Timer? _assetTimer, _locationTimer, _priceTimer;

    public event Action<Snapshot>? SnapshotChanged;
    public event Action<string>? SyncStatus;

    private void Status(string msg)
    {
        SyncStatus?.Invoke(msg);
        try
        {
            File.AppendAllText(System.IO.Path.Combine(_store.Root, "sync.log"),
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}");
        }
        catch { /* logging is best effort */ }
    }

    private sealed class CharState
    {
        public long SystemId { get; set; }
        public string SystemName { get; set; } = "";
        public string RegionName { get; set; } = "";
        public double Sec { get; set; }
        public string ShipName { get; set; } = "";
        public bool Docked { get; set; }
        public string? StationName { get; set; }
        public double Wallet { get; set; }
    }

    private sealed class RawStack
    {
        public long TypeId { get; set; }
        public long Qty { get; set; }
        public long StationId { get; set; }
        public OwnershipFlag Flag { get; set; }
    }

    private sealed class PriceEntry { public double Sell { get; set; } public double Buy { get; set; } }
    private sealed class PriceFile { public Dictionary<long, PriceEntry> Prices { get; set; } = new(); public DateTimeOffset? SyncedAt { get; set; } public string Hub { get; set; } = "jita"; }

    public SyncService()
    {
        var swCtor = System.Diagnostics.Stopwatch.StartNew();
        _universe = new UniverseService(_esi, _store);
        _settings = _store.Load<AppSettings>("settings.json");
        _auth = _store.Load<List<AuthRecord>>("characters.json");
        _charState = _store.Load<Dictionary<long, CharState>>("cache/charstate.json");
        var pf = _store.Load<PriceFile>("cache/prices.json");
        _prices = pf.Prices; _priceSyncedAt = pf.SyncedAt;
        _routes = _store.Load<Dictionary<string, int>>("cache/routes.json");
        _journal = _store.Load<Dictionary<string, JournalEntry>>("cache/journal.json");
        foreach (var a in _auth)
            _assetCache[a.CharacterId] = _store.Load<List<RawStack>>($"cache/assets-{a.CharacterId}.json").ToArray();
        UpdateSchedules();
        try
        {
            File.AppendAllText(System.IO.Path.Combine(_store.Root, "perf.log"),
                $"{DateTime.Now:HH:mm:ss.fff} ctorload {swCtor.ElapsedMilliseconds}ms{Environment.NewLine}");
        }
        catch { /* diagnostics only */ }
    }

    public AppSettings Settings => _settings;
    public bool HasLinkedCharacters => _auth.Count > 0;

    /// <summary>--demo forces the prototype dataset (used for documentation screenshots).</summary>
    private static readonly bool DemoMode = Environment.GetCommandLineArgs().Contains("--demo");

    // ---------- settings & credentials ----------

    public void SaveSettings(Action<AppSettings> mutate)
    {
        mutate(_settings);
        _store.Save("settings.json", _settings);
        UpdateSchedules();
        Broadcast();
    }

    public void SaveCredentials(string clientId, string secret)
    {
        _settings.ClientId = clientId.Trim();
        _settings.CredentialsVerified = false;
        _store.Save("settings.json", _settings);
        _store.Save("secret.json", new Dictionary<string, string> { ["secret"] = JsonStore.Protect(secret.Trim()) });
    }

    public string GetSecret()
    {
        var d = _store.Load<Dictionary<string, string>>("secret.json");
        return d.TryGetValue("secret", out var enc) ? JsonStore.Unprotect(enc) : "";
    }

    public async Task<(bool ok, string message)> TestConnection()
    {
        try
        {
            using var doc = await _esi.GetJson("/status/");
            var players = doc.RootElement.GetProperty("players").GetInt32();
            if (!string.IsNullOrEmpty(_settings.ClientId))
            {
                _settings.CredentialsVerified = true;
                _store.Save("settings.json", _settings);
            }
            Broadcast();
            return (true, $"ESI OK · {players:N0} players online");
        }
        catch (Exception ex) { return (false, "ESI unreachable: " + ex.Message); }
    }

    // ---------- auth ----------

    public async Task<(bool ok, string message)> LinkCharacter(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_settings.ClientId))
            return (false, "Set your ESI client ID in SETTINGS first.");
        try
        {
            var tokens = await _sso.Authenticate(_settings.ClientId, GetSecret(), _settings.CallbackPort, ct);
            var rec = _auth.FirstOrDefault(a => a.CharacterId == tokens.CharacterId);
            if (rec == null) { rec = new AuthRecord { CharacterId = tokens.CharacterId }; _auth.Add(rec); }
            rec.CharacterName = tokens.CharacterName;
            rec.AccessToken = tokens.AccessToken;
            rec.AccessTokenExpiresAt = tokens.ExpiresAt;
            rec.RefreshTokenEnc = JsonStore.Protect(tokens.RefreshToken);
            rec.Scopes = tokens.Scopes;
            _store.Save("characters.json", _auth);
            UpdateSchedules();
            Broadcast(); // show the linked character immediately; data fills in as sync progresses
            Status($"Linked {tokens.CharacterName} — syncing…");
            _ = SyncAll();
            return (true, $"Linked {tokens.CharacterName}. First sync running…");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public void Unlink(long charId)
    {
        _auth.RemoveAll(a => a.CharacterId == charId);
        _assetCache.Remove(charId);
        _charState.Remove(charId);
        _store.Save("characters.json", _auth);
        _store.Delete($"cache/assets-{charId}.json");
        _store.Save("cache/charstate.json", _charState);
        Broadcast();
    }

    private async Task<string?> Token(AuthRecord rec, CancellationToken ct)
    {
        if (rec.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return rec.AccessToken;
        var refresh = JsonStore.Unprotect(rec.RefreshTokenEnc);
        if (string.IsNullOrEmpty(refresh)) return null;
        try
        {
            var t = await _sso.Refresh(_settings.ClientId, GetSecret(), refresh, ct);
            rec.AccessToken = t.AccessToken;
            rec.AccessTokenExpiresAt = t.ExpiresAt;
            rec.RefreshTokenEnc = JsonStore.Protect(t.RefreshToken);
            _store.Save("characters.json", _auth);
            return t.AccessToken;
        }
        catch (Exception ex)
        {
            Status($"Token refresh failed for {rec.CharacterName}: {ex.Message}");
            return null;
        }
    }

    // ---------- sync ----------

    public async Task SyncAll(CancellationToken ct = default)
    {
        if (!await _syncLock.WaitAsync(0, ct)) return;
        try
        {
            foreach (var rec in _auth.ToList())
            {
                Status($"Syncing {rec.CharacterName}…");
                var token = await Token(rec, ct);
                if (token == null) continue;
                await SyncCharacter(rec, token, ct);
                rec.LastSyncAt = DateTimeOffset.UtcNow;
                Broadcast(); // assets/location/wallet for this character appear before prices finish
            }
            _store.Save("characters.json", _auth);
            await RefreshPricesInternal(ct);
            Broadcast();
            Status("Computing jump routes…");
            await ComputeRoutes(ct);
            _universe.Persist();
            Status("Sync complete.");
            Broadcast();
        }
        catch (Exception ex) { Status("Sync failed: " + ex.Message); }
        finally { _syncLock.Release(); Broadcast(); }
    }

    public async Task SyncOne(long charId, CancellationToken ct = default)
    {
        var rec = _auth.FirstOrDefault(a => a.CharacterId == charId);
        if (rec == null) return;
        var token = await Token(rec, ct);
        if (token == null) return;
        await SyncCharacter(rec, token, ct);
        rec.LastSyncAt = DateTimeOffset.UtcNow;
        _store.Save("characters.json", _auth);
        await ComputeRoutes(ct);
        _universe.Persist();
        Broadcast();
    }

    private async Task SyncCharacter(AuthRecord rec, string token, CancellationToken ct)
    {
        var id = rec.CharacterId;
        var state = _charState.TryGetValue(id, out var s) ? s : _charState[id] = new CharState();

        // location + ship
        await SyncLocationFor(rec, token, state, ct);

        // wallet balance
        try
        {
            var (bal, _) = await _esi.GetRaw($"/characters/{id}/wallet/", token, ct);
            state.Wallet = double.Parse(bal, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch { /* keep last */ }

        // journal (accumulated beyond ESI's 30-day window)
        try
        {
            var entries = await _esi.GetPaged($"/characters/{id}/wallet/journal/", token, ct);
            var txById = new Dictionary<long, (long typeId, long qty, long locationId, bool isBuy)>();
            try
            {
                var (txBody, _) = await _esi.GetRaw($"/characters/{id}/wallet/transactions/", token, ct);
                using var txDoc = JsonDocument.Parse(txBody);
                foreach (var tx in txDoc.RootElement.EnumerateArray())
                    txById[tx.GetProperty("transaction_id").GetInt64()] =
                        (tx.GetProperty("type_id").GetInt64(), tx.GetProperty("quantity").GetInt64(),
                         tx.GetProperty("location_id").GetInt64(), tx.GetProperty("is_buy").GetBoolean());
            }
            catch { /* transactions are optional enrichment */ }

            foreach (var e in entries)
            {
                var jid = id + ":" + e.GetProperty("id").GetInt64();
                if (_journal.ContainsKey(jid)) continue;
                var refType = e.GetProperty("ref_type").GetString() ?? "";
                var amount = e.TryGetProperty("amount", out var am) ? am.GetDouble() : 0;
                var desc = e.TryGetProperty("description", out var de) ? de.GetString() ?? "" : "";
                long? stationId = null;
                if (e.TryGetProperty("context_id_type", out var cit) && e.TryGetProperty("context_id", out var civ))
                {
                    var ctxType = cit.GetString();
                    var ctxId = civ.GetInt64();
                    if (ctxType is "station_id" or "structure_id") stationId = ctxId;
                    else if (ctxType == "market_transaction_id" && txById.TryGetValue(ctxId, out var tx))
                    {
                        stationId = tx.locationId;
                        if (_universe.Cache.Types.TryGetValue(tx.typeId, out var te))
                            desc = $"{(tx.isBuy ? "Bought" : "Sold")} {te.Name} × {tx.qty:N0}";
                    }
                }
                _journal[jid] = new JournalEntry
                {
                    Id = jid, Ts = e.GetProperty("date").GetDateTimeOffset(), CharId = id,
                    Desc = desc, RefType = refType, Amount = amount, StationId = stationId,
                };
            }
            // resolve station names referenced only by journal entries (e.g. market hubs the
            // character has no assets in) so the wallet screens never show raw location ids
            foreach (var sid in _journal.Values.Where(j => j.StationId is > 0).Select(j => j.StationId!.Value).Distinct())
            {
                if (_universe.Cache.Stations.ContainsKey(sid)) continue;
                var st = await _universe.ResolveLocation(sid, token, _settings.CitadelPolicy, ct);
                if (st != null) await _universe.ResolveSystem(st.SystemId, ct);
            }
            _store.Save("cache/journal.json", _journal);
        }
        catch (Exception ex) { Status($"Journal sync failed: {ex.Message}"); }

        // assets
        try
        {
            Status($"Fetching assets for {rec.CharacterName}…");
            var items = await _esi.GetPaged($"/characters/{id}/assets/", token, ct);
            Status($"{items.Count:N0} asset items · resolving types & stations…");
            var byItemId = items.ToDictionary(i => i.GetProperty("item_id").GetInt64());
            var stacks = new Dictionary<(long type, long station, OwnershipFlag flag), long>();
            var typeIds = new HashSet<long>();

            foreach (var item in items)
            {
                var typeId = item.GetProperty("type_id").GetInt64();
                typeIds.Add(typeId);
                var qty = item.GetProperty("quantity").GetInt64();

                // walk up the container/ship chain to a station, structure or solar system
                var cur = item;
                var flag = OwnershipFlag.Hangar;
                var depth = 0;
                while (byItemId.TryGetValue(cur.GetProperty("location_id").GetInt64(), out var parent) && depth++ < 16)
                {
                    if (depth == 1)
                    {
                        var lf = cur.GetProperty("location_flag").GetString() ?? "";
                        flag = IsFittingFlag(lf) ? OwnershipFlag.Fitted : OwnershipFlag.Container;
                    }
                    cur = parent;
                }
                var rootLoc = cur.GetProperty("location_id").GetInt64();
                var rootType = cur.GetProperty("location_type").GetString();
                long stationId;
                if (rootType == "solar_system") stationId = -rootLoc; // synthetic "in space" station keyed by system
                else stationId = rootLoc;

                var key = (typeId, stationId, flag);
                stacks[key] = stacks.GetValueOrDefault(key) + qty;
            }

            await _universe.ResolveTypes(typeIds, ct);

            var raw = stacks
                .Where(kv => !ShouldHide(kv.Key.type))
                .Select(kv => new RawStack { TypeId = kv.Key.type, StationId = kv.Key.station, Flag = kv.Key.flag, Qty = kv.Value })
                .ToArray();
            _assetCache[id] = raw;
            _store.Save($"cache/assets-{id}.json", raw.ToList());

            // resolve station names & systems
            foreach (var stId in raw.Select(r => r.StationId).Distinct())
            {
                if (stId < 0) { await _universe.ResolveSystem(-stId, ct); continue; }
                var st = await _universe.ResolveLocation(stId, token, _settings.CitadelPolicy, ct);
                if (st != null) await _universe.ResolveSystem(st.SystemId, ct);
            }
        }
        catch (Exception ex) { Status($"Asset sync failed for {rec.CharacterName}: {ex.Message}"); }

        _store.Save("cache/charstate.json", _charState);
    }

    private bool ShouldHide(long typeId) => false;

    private static bool IsFittingFlag(string f) =>
        f.StartsWith("HiSlot") || f.StartsWith("MedSlot") || f.StartsWith("LoSlot") ||
        f.StartsWith("RigSlot") || f.StartsWith("SubSystemSlot") || f is "DroneBay" or "FighterBay";

    private async Task SyncLocationFor(AuthRecord rec, string token, CharState state, CancellationToken ct)
    {
        var id = rec.CharacterId;
        try
        {
            using var loc = await _esi.GetJson($"/characters/{id}/location/", token, ct);
            var sysId = loc.RootElement.GetProperty("solar_system_id").GetInt64();
            state.SystemId = sysId;
            long? stId = loc.RootElement.TryGetProperty("station_id", out var sid) ? sid.GetInt64() :
                         loc.RootElement.TryGetProperty("structure_id", out var strid) ? strid.GetInt64() : null;
            state.Docked = stId != null;
            if (stId != null)
            {
                var st = await _universe.ResolveLocation(stId.Value, token, _settings.CitadelPolicy, ct);
                state.StationName = st?.Name?.Replace(" - ", " · ").ToUpperInvariant();
            }
            else state.StationName = null;

            var sys = await _universe.ResolveSystem(sysId, ct);
            if (sys != null)
            {
                state.SystemName = sys.Name;
                state.Sec = sys.Sec;
                state.RegionName = _universe.RegionName(sysId);
            }

            using var ship = await _esi.GetJson($"/characters/{id}/ship/", token, ct);
            var shipTypeId = ship.RootElement.GetProperty("ship_type_id").GetInt64();
            await _universe.ResolveTypes(new[] { shipTypeId }, ct);
            state.ShipName = _universe.Cache.Types.TryGetValue(shipTypeId, out var te) ? te.Name : $"Type {shipTypeId}";
        }
        catch (Exception ex) { Status($"Location sync failed for {rec.CharacterName}: {ex.Message}"); }
    }

    public async Task SyncLocations(CancellationToken ct = default)
    {
        var moved = false;
        foreach (var rec in _auth.ToList())
        {
            var token = await Token(rec, ct);
            if (token == null) continue;
            var state = _charState.TryGetValue(rec.CharacterId, out var s) ? s : _charState[rec.CharacterId] = new CharState();
            var before = state.SystemId;
            await SyncLocationFor(rec, token, state, ct);
            if (state.SystemId != before) moved = true;
        }
        _store.Save("cache/charstate.json", _charState);
        if (moved) await ComputeRoutes(ct);
        Broadcast();
    }

    // ---------- prices ----------

    public async Task RefreshPrices(CancellationToken ct = default)
    {
        await RefreshPricesInternal(ct);
        Broadcast();
    }

    private async Task RefreshPricesInternal(CancellationToken ct)
    {
        var hub = PriceHubDef.ByKey(_settings.PriceHub);
        var typeIds = _assetCache.Values.SelectMany(v => v).Select(r => r.TypeId).Distinct().ToList();
        if (typeIds.Count == 0) return;
        Status($"Fetching {typeIds.Count} prices from {hub.Label}…");

        var tasks = typeIds.Select(async tid =>
        {
            try
            {
                var orders = await _esi.GetPaged($"/markets/{hub.RegionId}/orders/?type_id={tid}", null, ct);
                double bestSell = 0, bestBuy = 0;
                foreach (var o in orders)
                {
                    var isBuy = o.GetProperty("is_buy_order").GetBoolean();
                    var price = o.GetProperty("price").GetDouble();
                    var loc = o.GetProperty("location_id").GetInt64();
                    if (!isBuy && loc == hub.StationId && (bestSell == 0 || price < bestSell)) bestSell = price;
                    if (isBuy && price > bestBuy) bestBuy = price;
                }
                return (tid, sell: bestSell, buy: bestBuy);
            }
            catch { return (tid, sell: 0d, buy: 0d); }
        }).ToList();

        foreach (var t in tasks)
        {
            var (tid, sell, buy) = await t;
            if (sell > 0 || buy > 0) _prices[tid] = new PriceEntry { Sell = sell, Buy = buy };
        }
        _priceSyncedAt = DateTimeOffset.UtcNow;
        _store.Save("cache/prices.json", new PriceFile { Prices = _prices, SyncedAt = _priceSyncedAt, Hub = hub.Key });
    }

    // ---------- routes ----------

    private async Task ComputeRoutes(CancellationToken ct)
    {
        var destSystems = new HashSet<long>();
        foreach (var raw in _assetCache.Values.SelectMany(v => v))
        {
            if (raw.StationId < 0) destSystems.Add(-raw.StationId);
            else if (_universe.Cache.Stations.TryGetValue(raw.StationId, out var st)) destSystems.Add(st.SystemId);
        }
        var origins = _charState.Values.Select(s => s.SystemId).Where(x => x != 0).Distinct().ToList();

        foreach (var o in origins)
        {
            foreach (var d in destSystems)
            {
                var key = $"{o}-{d}";
                if (o == d) { _routes[key] = 0; continue; }
                if (_routes.ContainsKey(key)) continue;
                try
                {
                    using var doc = await _esi.GetJson($"/route/{o}/{d}/", null, ct);
                    _routes[key] = doc.RootElement.GetArrayLength() - 1;
                }
                catch { _routes[key] = -1; }
            }
        }
        _store.Save("cache/routes.json", _routes);
    }

    // ---------- snapshot ----------

    public Snapshot BuildSnapshot()
    {
        if (_auth.Count == 0 || DemoMode) return DemoData.Build();
        var swSnap = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            return BuildSnapshotCore();
        }
        finally
        {
            try
            {
                File.AppendAllText(System.IO.Path.Combine(_store.Root, "perf.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} snapshot {swSnap.ElapsedMilliseconds}ms{Environment.NewLine}");
            }
            catch { /* diagnostics only */ }
        }
    }

    private long _cachedDirSize = -1;
    private DateTimeOffset _cachedDirSizeAt;

    private Snapshot BuildSnapshotCore()
    {
        if (_cachedDirSize < 0 || DateTimeOffset.UtcNow - _cachedDirSizeAt > TimeSpan.FromSeconds(60))
        {
            _cachedDirSize = _store.CacheSizeBytes();
            _cachedDirSizeAt = DateTimeOffset.UtcNow;
        }

        // one pass over the accumulated journal for all per-character 30-day deltas
        var last30Cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var deltas = new Dictionary<long, double>();
        foreach (var j in _journal.Values)
            if (j.Ts >= last30Cutoff)
                deltas[j.CharId] = deltas.GetValueOrDefault(j.CharId) + j.Amount;

        var snap = new Snapshot { Demo = false, PriceSyncedAt = _priceSyncedAt, CacheSizeBytes = _cachedDirSize };

        for (var i = 0; i < _auth.Count; i++)
        {
            var a = _auth[i];
            var st = _charState.GetValueOrDefault(a.CharacterId);
            snap.Characters.Add(new CharacterInfo
            {
                Id = a.CharacterId,
                Name = a.CharacterName,
                Color = CharacterPalette.Colors[i % CharacterPalette.Colors.Length],
                SystemId = st?.SystemId,
                SystemName = st?.SystemName,
                RegionName = st?.RegionName,
                SecStatus = st?.Sec,
                ShipName = st?.ShipName,
                Docked = st?.Docked ?? false,
                StationName = st?.StationName,
                ScopesGranted = a.Scopes,
                TokenExpiresAt = a.AccessTokenExpiresAt,
                LastSyncAt = a.LastSyncAt,
                WalletBalance = st?.Wallet ?? 0,
                WalletDelta30d = deltas.GetValueOrDefault(a.CharacterId),
            });
        }

        foreach (var (charId, raws) in _assetCache)
        {
            foreach (var r in raws)
            {
                var te = _universe.Cache.Types.GetValueOrDefault(r.TypeId);
                var pe = _prices.GetValueOrDefault(r.TypeId);
                if (!snap.Stations.TryGetValue(r.StationId, out var stInfo))
                    snap.Stations[r.StationId] = stInfo = BuildStation(r.StationId);
                var name = te?.Name ?? $"Type {r.TypeId}";
                var group = te?.Group ?? "";
                snap.Assets.Add(new AssetStack
                {
                    TypeId = r.TypeId,
                    Name = name,
                    Group = group,
                    Category = te?.Category ?? "",
                    Qty = r.Qty,
                    CharId = charId,
                    StationId = r.StationId,
                    Flag = r.Flag,
                    Sell = pe?.Sell ?? 0,
                    Buy = pe?.Buy ?? 0,
                    Search = $"{name} {group} {stInfo.Name}".ToLowerInvariant(),
                });
            }
        }

        snap.Journal = _journal.Values.OrderByDescending(j => j.Ts).Take(500).ToList();
        foreach (var j in snap.Journal)
        {
            if (j.StationId is { } sid && !snap.Stations.ContainsKey(sid) && _universe.Cache.Stations.ContainsKey(sid))
                snap.Stations[sid] = BuildStation(sid);
        }
        return snap;
    }

    private StationInfo BuildStation(long stationId)
    {
        long sysId;
        string name;
        if (stationId < 0)
        {
            sysId = -stationId;
            var sysName = _universe.Cache.Systems.GetValueOrDefault(sysId)?.Name ?? $"System {sysId}";
            name = $"{sysName} · In Space";
        }
        else
        {
            var st = _universe.Cache.Stations.GetValueOrDefault(stationId);
            sysId = st?.SystemId ?? 0;
            // design style: middots instead of EVE's verbose " - " chains
            name = st?.Name.Replace(" - ", " · ") ?? $"Location {stationId}";
        }
        var info = new StationInfo
        {
            Id = stationId,
            Name = name,
            SystemId = sysId,
            SystemName = _universe.Cache.Systems.GetValueOrDefault(sysId)?.Name ?? "",
            RegionName = sysId != 0 ? _universe.RegionName(sysId) : "Unknown Region",
        };
        foreach (var (charId, st) in _charState)
        {
            if (st.SystemId == 0 || sysId == 0) continue;
            info.Jumps[charId] = st.SystemId == sysId ? 0 : _routes.GetValueOrDefault($"{st.SystemId}-{sysId}", -1);
        }
        return info;
    }

    public void PurgeCache()
    {
        _store.PurgeCache();
        _universe.Reset();
        _assetCache.Clear();
        _prices = new Dictionary<long, PriceEntry>();
        _routes = new Dictionary<string, int>();
        _journal = new Dictionary<string, JournalEntry>();
        _charState = new Dictionary<long, CharState>();
        _priceSyncedAt = null;
        Broadcast();
    }

    // ---------- scheduling ----------

    private void UpdateSchedules()
    {
        _assetTimer?.Dispose(); _locationTimer?.Dispose(); _priceTimer?.Dispose();
        _assetTimer = MakeTimer(TimeSpan.FromMinutes(_settings.AssetSyncMin), () => _ = SyncAll());
        _locationTimer = MakeTimer(TimeSpan.FromSeconds(_settings.LocationTrackSec), () => _ = SyncLocations());
        _priceTimer = MakeTimer(TimeSpan.FromMinutes(_settings.PriceRefreshMin), () => _ = RefreshPrices());
    }

    private System.Timers.Timer? MakeTimer(TimeSpan interval, Action tick)
    {
        if (interval <= TimeSpan.Zero || _auth.Count == 0 || DemoMode) return null;
        var t = new System.Timers.Timer(interval.TotalMilliseconds) { AutoReset = true };
        t.Elapsed += (_, _) => tick();
        t.Start();
        return t;
    }

    private void Broadcast() => SnapshotChanged?.Invoke(BuildSnapshot());

    public void Start()
    {
        Broadcast();
        if (_auth.Count > 0 && !DemoMode) _ = SyncAll();
    }

    public void Dispose()
    {
        _assetTimer?.Dispose(); _locationTimer?.Dispose(); _priceTimer?.Dispose();
    }
}

