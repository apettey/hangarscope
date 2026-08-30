using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HangarScope.Models;
using HangarScope.Services;

namespace HangarScope.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly SyncService _sync;
    private Snapshot _snap = new();
    private bool _loadingUi;

    // ---- UI state (mirrors the prototype's state object) ----
    private string _screen = "assets";
    private readonly HashSet<long> _selChars = new();
    private string _priceSide;
    private long _distFrom; // 0 = closest character
    private string _region = "All";
    private string _category = "All";
    private string _flag = "All";
    private double _minValue;
    private long? _selStation;
    private string _sortKey = "value";
    private int _sortDir = -1;
    private long? _walletChar;

    private static readonly Dictionary<string, IBrush> BrushCache = new();
    public static IBrush B(string hex)
    {
        if (!BrushCache.TryGetValue(hex, out var b))
            BrushCache[hex] = b = new SolidColorBrush(Avalonia.Media.Color.Parse(hex));
        return b;
    }

    public MainViewModel(SyncService sync)
    {
        _sync = sync;
        _priceSide = sync.Settings.DefaultPriceSide == "buy" ? "buy" : "sell";
        _sync.SnapshotChanged += snap => Dispatcher.UIThread.Post(() => { _snap = snap; Recompute(); });
        _sync.SyncStatus += msg => Dispatcher.UIThread.Post(() => SyncStatusText = msg.ToUpperInvariant());
        _snap = sync.BuildSnapshot();
        LoadSettingsUi();
        Recompute();
    }

    // =========================== global chrome ===========================

    [ObservableProperty] private List<TabVm> _tabs = new();
    [ObservableProperty] private bool _isAssets;
    [ObservableProperty] private bool _isDash;
    [ObservableProperty] private bool _isWallet;
    [ObservableProperty] private bool _isLoc;
    [ObservableProperty] private bool _isAuth;
    [ObservableProperty] private bool _isSettings;
    [ObservableProperty] private string _netWorthText = "";
    [ObservableProperty] private bool _sellActive = true;
    [ObservableProperty] private bool _buyActive;
    [ObservableProperty] private string _syncStatusText = "";

    [RelayCommand] private void SelectTab(string id) { _screen = id; Recompute(); }
    [RelayCommand] private void SetSell() { _priceSide = "sell"; Recompute(); }
    [RelayCommand] private void SetBuy() { _priceSide = "buy"; Recompute(); }

    // =========================== assets screen ===========================

    private string _searchText = "";
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) Recompute(); } }

    [ObservableProperty] private List<ChipVm> _charChips = new();
    [ObservableProperty] private List<TreeRegionVm> _tree = new();
    [ObservableProperty] private List<AssetRowVm> _rows = new();
    [ObservableProperty] private string _rowCountText = "";
    [ObservableProperty] private string _filteredValueText = "";
    [ObservableProperty] private string _priceStatusText = "";
    [ObservableProperty] private string _indName = "";
    [ObservableProperty] private string _indQty = "";
    [ObservableProperty] private string _indJumps = "";
    [ObservableProperty] private string _indValue = "";

    public ObservableCollection<OptionVm> DistFromOptions { get; } = new();
    public ObservableCollection<OptionVm> RegionOptions { get; } = new();
    public ObservableCollection<OptionVm> CategoryOptions { get; } = new(
        new[] { "All", "Ships", "Modules", "Minerals", "Ammunition", "Drones", "Blueprints", "Commodities", "Implants" }
            .Select(c => new OptionVm(c, c)));
    public ObservableCollection<OptionVm> FlagOptions { get; } = new(new[]
    {
        new OptionVm("All", "All"), new OptionVm("Hangar", "Hangar"),
        new OptionVm("Fitted", "Fitted"), new OptionVm("In container", "Container"),
    });
    public ObservableCollection<OptionVm> MinValueOptions { get; } = new(new[]
    {
        new OptionVm("Any", 0d), new OptionVm("> 1 M", 1e6), new OptionVm("> 10 M", 1e7),
        new OptionVm("> 100 M", 1e8), new OptionVm("> 1 B", 1e9),
    });

    private int _distFromIndex;
    public int DistFromIndex
    {
        get => _distFromIndex;
        set { if (SetProperty(ref _distFromIndex, value) && !_loadingUi) { _distFrom = value <= 0 ? 0 : (long)(DistFromOptions.ElementAtOrDefault(value)?.Value ?? 0L); Recompute(); } }
    }

    private int _regionIndex;
    public int RegionIndex
    {
        get => _regionIndex;
        set { if (SetProperty(ref _regionIndex, value) && !_loadingUi) { _region = (string)(RegionOptions.ElementAtOrDefault(Math.Max(0, value))?.Value ?? "All"); Recompute(); } }
    }

    private int _categoryIndex;
    public int CategoryIndex
    {
        get => _categoryIndex;
        set { if (SetProperty(ref _categoryIndex, value) && !_loadingUi) { _category = (string)(CategoryOptions.ElementAtOrDefault(Math.Max(0, value))?.Value ?? "All"); Recompute(); } }
    }

    private int _flagIndex;
    public int FlagIndex
    {
        get => _flagIndex;
        set { if (SetProperty(ref _flagIndex, value) && !_loadingUi) { _flag = (string)(FlagOptions.ElementAtOrDefault(Math.Max(0, value))?.Value ?? "All"); Recompute(); } }
    }

    private int _minValueIndex;
    public int MinValueIndex
    {
        get => _minValueIndex;
        set { if (SetProperty(ref _minValueIndex, value) && !_loadingUi) { _minValue = (double)(MinValueOptions.ElementAtOrDefault(Math.Max(0, value))?.Value ?? 0d); Recompute(); } }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _loadingUi = true;
        SearchText = "";
        _selChars.Clear();
        _region = "All"; RegionIndex = 0;
        _category = "All"; CategoryIndex = 0;
        _flag = "All"; FlagIndex = 0;
        _minValue = 0; MinValueIndex = 0;
        _selStation = null;
        _loadingUi = false;
        Recompute();
    }

    [RelayCommand] private void SortBy(string key)
    {
        _sortDir = _sortKey == key ? -_sortDir : (key is "name" or "jumps" ? 1 : -1);
        _sortKey = key;
        Recompute();
    }

    // =========================== dashboard ===========================

    [ObservableProperty] private string _priceLabelText = "SELL";
    [ObservableProperty] private List<CharCardVm> _charCards = new();
    [ObservableProperty] private List<BarVm> _regionBars = new();
    [ObservableProperty] private List<TopRowVm> _topRows = new();

    // =========================== wallet ===========================

    [ObservableProperty] private string _walletTotalText = "";
    [ObservableProperty] private string _grandTotalText = "";
    [ObservableProperty] private List<WalletCardVm> _walletCards = new();
    [ObservableProperty] private List<ChipVm> _walletChips = new();
    [ObservableProperty] private List<BarVm> _flowBars = new();
    [ObservableProperty] private string _net30Text = "";
    [ObservableProperty] private IBrush _net30Brush = B("#8bd450");
    [ObservableProperty] private List<StationFlowVm> _stationFlows = new();
    [ObservableProperty] private string _spaceFlowText = "";
    [ObservableProperty] private List<JournalRowVm> _journalRows = new();
    [ObservableProperty] private string _journalScopeText = "";

    // =========================== locations / auth ===========================

    [ObservableProperty] private List<LocCardVm> _locCards = new();
    [ObservableProperty] private List<AuthRowVm> _authRows = new();
    [ObservableProperty] private string _linkStatusText = "";

    [RelayCommand]
    private async Task LinkCharacter()
    {
        LinkStatusText = "WAITING FOR EVE SSO IN YOUR BROWSER…";
        var (ok, message) = await _sync.LinkCharacter(CancellationToken.None);
        LinkStatusText = message.ToUpperInvariant();
    }

    // =========================== settings ===========================

    [ObservableProperty] private string _clientIdInput = "";
    [ObservableProperty] private string _secretInput = "";
    [ObservableProperty] private bool _showSecret;
    [ObservableProperty] private string _callbackUrl = "";
    [ObservableProperty] private bool _credentialsVerified;
    [ObservableProperty] private string _settingsStatusText = "";
    [ObservableProperty] private string _cacheSizeText = "";

    public ObservableCollection<OptionVm> PriceHubOptions { get; } = new(new[]
    {
        new OptionVm("Jita 4-4", "jita"), new OptionVm("Amarr", "amarr"),
        new OptionVm("Dodixie", "dodixie"), new OptionVm("Rens", "rens"),
    });
    public ObservableCollection<OptionVm> PriceSideOptions { get; } = new(new[]
    {
        new OptionVm("Sell orders", "sell"), new OptionVm("Buy orders", "buy"), new OptionVm("Split average", "split"),
    });
    public ObservableCollection<OptionVm> PriceRefreshOptions { get; } = new(new[]
    {
        new OptionVm("Every 30 min", 30), new OptionVm("Every hour", 60), new OptionVm("Manual", 0),
    });
    public ObservableCollection<OptionVm> AssetSyncOptions { get; } = new(new[]
    {
        new OptionVm("Every hour (ESI cache)", 60), new OptionVm("Every 6 hours", 360), new OptionVm("Manual", 0),
    });
    public ObservableCollection<OptionVm> LocationTrackOptions { get; } = new(new[]
    {
        new OptionVm("Live (30 s)", 30), new OptionVm("Every 5 min", 300), new OptionVm("Off", 0),
    });
    public ObservableCollection<OptionVm> CitadelOptions { get; } = new(new[]
    {
        new OptionVm("When docked access", "docked"), new OptionVm("Always try", "always"), new OptionVm("Off", "off"),
    });

    private int _priceHubIndex, _priceSideIndex, _priceRefreshIndex, _assetSyncIndex, _locationTrackIndex, _citadelIndex;
    public int PriceHubIndex { get => _priceHubIndex; set { if (SetProperty(ref _priceHubIndex, value) && !_loadingUi) SaveSetting(s => s.PriceHub = (string)PriceHubOptions[Math.Max(0, value)].Value!); } }
    public int PriceSideIndex { get => _priceSideIndex; set { if (SetProperty(ref _priceSideIndex, value) && !_loadingUi) SaveSetting(s => s.DefaultPriceSide = (string)PriceSideOptions[Math.Max(0, value)].Value!); } }
    public int PriceRefreshIndex { get => _priceRefreshIndex; set { if (SetProperty(ref _priceRefreshIndex, value) && !_loadingUi) SaveSetting(s => s.PriceRefreshMin = (int)PriceRefreshOptions[Math.Max(0, value)].Value!); } }
    public int AssetSyncIndex { get => _assetSyncIndex; set { if (SetProperty(ref _assetSyncIndex, value) && !_loadingUi) SaveSetting(s => s.AssetSyncMin = (int)AssetSyncOptions[Math.Max(0, value)].Value!); } }
    public int LocationTrackIndex { get => _locationTrackIndex; set { if (SetProperty(ref _locationTrackIndex, value) && !_loadingUi) SaveSetting(s => s.LocationTrackSec = (int)LocationTrackOptions[Math.Max(0, value)].Value!); } }
    public int CitadelIndex { get => _citadelIndex; set { if (SetProperty(ref _citadelIndex, value) && !_loadingUi) SaveSetting(s => s.CitadelPolicy = (string)CitadelOptions[Math.Max(0, value)].Value!); } }

    private void SaveSetting(Action<AppSettings> mutate) => _sync.SaveSettings(mutate);

    [RelayCommand] private void ToggleShowSecret() => ShowSecret = !ShowSecret;

    [RelayCommand]
    private async Task TestConnection()
    {
        SettingsStatusText = "TESTING…";
        var (ok, message) = await _sync.TestConnection();
        SettingsStatusText = message.ToUpperInvariant();
        CredentialsVerified = _sync.Settings.CredentialsVerified;
    }

    [RelayCommand]
    private void SaveCredentials()
    {
        _sync.SaveCredentials(ClientIdInput, SecretInput);
        SettingsStatusText = "CREDENTIALS SAVED · ENCRYPTED LOCALLY";
        CredentialsVerified = false;
    }

    [RelayCommand]
    private void PurgeCache()
    {
        _sync.PurgeCache();
        SettingsStatusText = "CACHE PURGED";
    }

    private void LoadSettingsUi()
    {
        _loadingUi = true;
        var s = _sync.Settings;
        ClientIdInput = s.ClientId;
        CallbackUrl = $"http://localhost:{s.CallbackPort}/sso/callback";
        CredentialsVerified = s.CredentialsVerified;
        PriceHubIndex = Math.Max(0, PriceHubOptions.ToList().FindIndex(o => (string)o.Value! == s.PriceHub));
        PriceSideIndex = Math.Max(0, PriceSideOptions.ToList().FindIndex(o => (string)o.Value! == s.DefaultPriceSide));
        PriceRefreshIndex = Math.Max(0, PriceRefreshOptions.ToList().FindIndex(o => (int)o.Value! == s.PriceRefreshMin));
        AssetSyncIndex = Math.Max(0, AssetSyncOptions.ToList().FindIndex(o => (int)o.Value! == s.AssetSyncMin));
        LocationTrackIndex = Math.Max(0, LocationTrackOptions.ToList().FindIndex(o => (int)o.Value! == s.LocationTrackSec));
        CitadelIndex = Math.Max(0, CitadelOptions.ToList().FindIndex(o => (string)o.Value! == s.CitadelPolicy));
        _loadingUi = false;
    }

    // =========================== recompute (the prototype's renderVals) ===========================

    private double Unit(AssetStack a) => _priceSide == "sell" ? a.Sell : a.Buy;
    private double Val(AssetStack a) => a.Qty * Unit(a);

    private int Jumps(AssetStack a)
    {
        if (!_snap.Stations.TryGetValue(a.StationId, out var st)) return -1;
        if (_distFrom != 0) return st.Jumps.GetValueOrDefault(_distFrom, -1);
        var active = _selChars.Count > 0 ? (IEnumerable<long>)_selChars : _snap.Characters.Select(c => c.Id);
        var vals = active.Select(c => st.Jumps.GetValueOrDefault(c, -1)).Where(j => j >= 0).ToList();
        return vals.Count > 0 ? vals.Min() : -1;
    }

    private void Recompute()
    {
        var snap = _snap;
        var charById = snap.Characters.ToDictionary(c => c.Id);
        var active = _selChars.Count > 0 ? _selChars.Where(charById.ContainsKey).ToHashSet() : charById.Keys.ToHashSet();
        var q = _searchText.Trim().ToLowerInvariant();

        IsAssets = _screen == "assets"; IsDash = _screen == "dash"; IsWallet = _screen == "wallet";
        IsLoc = _screen == "loc"; IsAuth = _screen == "auth"; IsSettings = _screen == "settings";
        Tabs = new (string id, string label)[] { ("assets", "ASSETS"), ("dash", "DASHBOARD"), ("wallet", "WALLET"), ("loc", "LOCATIONS"), ("auth", "CHARACTERS"), ("settings", "SETTINGS") }
            .Select(t => new TabVm(t.id, t.label, _screen == t.id, new RelayCommand(() => SelectTab(t.id)))).ToList();

        SellActive = _priceSide == "sell"; BuyActive = _priceSide == "buy";
        PriceLabelText = _priceSide.ToUpperInvariant();
        NetWorthText = Formatting.Isk(snap.Assets.Sum(Val));

        string StationName(long id) => snap.Stations.TryGetValue(id, out var st) ? st.Name : $"Location {id}";
        string StationRegion(long id) => snap.Stations.TryGetValue(id, out var st) ? st.RegionName : "Unknown Region";

        // ---- assets: filter + sort ----
        var charFiltered = snap.Assets.Where(a => active.Contains(a.CharId)).ToList();
        var filtered = charFiltered.Where(a =>
        {
            if (_region != "All" && StationRegion(a.StationId) != _region) return false;
            if (_category != "All" && a.Category != _category) return false;
            if (_flag != "All" && a.Flag.ToString() != _flag) return false;
            if (Val(a) < _minValue) return false;
            if (_selStation is { } sel && a.StationId != sel) return false;
            if (q.Length > 0 && !($"{a.Name} {a.Group} {StationName(a.StationId)}").ToLowerInvariant().Contains(q)) return false;
            return true;
        }).ToList();

        var sorted = filtered.OrderBy(a => 0);
        sorted = (_sortKey, _sortDir) switch
        {
            ("name", 1) => filtered.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase),
            ("name", _) => filtered.OrderByDescending(a => a.Name, StringComparer.OrdinalIgnoreCase),
            ("qty", 1) => filtered.OrderBy(a => a.Qty),
            ("qty", _) => filtered.OrderByDescending(a => a.Qty),
            ("jumps", 1) => filtered.OrderBy(Jumps),
            ("jumps", _) => filtered.OrderByDescending(Jumps),
            (_, 1) => filtered.OrderBy(Val),
            _ => filtered.OrderByDescending(Val),
        };

        Rows = sorted.Select(a =>
        {
            var j = Jumps(a);
            var c = charById[a.CharId];
            return new AssetRowVm(
                a.Name,
                a.Flag == OwnershipFlag.Container ? "CTNR" : a.Flag.ToString().ToUpperInvariant(),
                a.Flag != OwnershipFlag.Hangar,
                a.Flag == OwnershipFlag.Fitted,
                Formatting.Qty(a.Qty), a.Group, c.Name, B(c.Color),
                StationName(a.StationId),
                j < 0 ? "—" : j.ToString(),
                j == 0 ? B("#8bd450") : j < 0 ? B("#6b7887") : j <= 10 ? B("#cfd8e0") : B("#c0533f"),
                Formatting.Isk(Unit(a)), Formatting.Isk(Val(a)));
        }).ToList();

        string Ind(string k) => _sortKey == k ? (_sortDir == 1 ? " ▲" : " ▼") : "";
        IndName = Ind("name"); IndQty = Ind("qty"); IndJumps = Ind("jumps"); IndValue = Ind("value");
        RowCountText = Rows.Count.ToString();
        FilteredValueText = Formatting.Isk(filtered.Sum(Val));

        var hub = PriceHubDef.ByKey(_sync.Settings.PriceHub);
        PriceStatusText = $"PRICES · ESI MARKET · {hub.Label} · SYNCED {Formatting.Ago(snap.PriceSyncedAt).ToUpperInvariant()}";

        // ---- location tree (respects char + search filters, not station selection) ----
        var treeSource = charFiltered.Where(a => q.Length == 0
            || $"{a.Name} {a.Group}".ToLowerInvariant().Contains(q)
            || StationName(a.StationId).ToLowerInvariant().Contains(q)).ToList();
        Tree = treeSource
            .GroupBy(a => StationRegion(a.StationId))
            .Select(g => (name: g.Key, value: g.Sum(Val), stations: g.GroupBy(a => a.StationId).ToList()))
            .OrderByDescending(g => g.value)
            .Select(g => new TreeRegionVm(g.name.ToUpperInvariant(), Formatting.Isk(g.value),
                g.stations.Select(sg => new TreeStationVm(sg.Key, StationName(sg.Key), sg.Count().ToString(),
                    _selStation == sg.Key,
                    new RelayCommand(() => { _selStation = _selStation == sg.Key ? null : sg.Key; Recompute(); }))).ToList()))
            .ToList();

        // ---- character chips ----
        var chips = new List<ChipVm>
        {
            new("All characters", Formatting.Isk(snap.Assets.Sum(Val)), B("#6b7887"), _selChars.Count == 0,
                new RelayCommand(() => { _selChars.Clear(); Recompute(); })),
        };
        chips.AddRange(snap.Characters.Select(c => new ChipVm(
            c.Name, Formatting.Isk(snap.Assets.Where(a => a.CharId == c.Id).Sum(Val)), B(c.Color), _selChars.Contains(c.Id),
            new RelayCommand(() =>
            {
                if (!_selChars.Remove(c.Id)) _selChars.Add(c.Id);
                Recompute();
            }))));
        CharChips = chips;

        // ---- selects with dynamic contents ----
        SyncOptions(DistFromOptions,
            new[] { new OptionVm("Closest character", 0L) }.Concat(snap.Characters.Select(c => new OptionVm(c.Name, c.Id))).ToList(),
            ref _distFromIndex, nameof(DistFromIndex));
        SyncOptions(RegionOptions,
            new[] { new OptionVm("All", "All") }.Concat(snap.Stations.Values.Select(s => s.RegionName).Distinct().OrderBy(r => r).Select(r => new OptionVm(r, r))).ToList(),
            ref _regionIndex, nameof(RegionIndex));

        // ---- dashboard ----
        CharCards = snap.Characters.Select(c =>
        {
            var mine = snap.Assets.Where(a => a.CharId == c.Id).ToList();
            var top = mine.OrderByDescending(Val).FirstOrDefault();
            return new CharCardVm(c.Name.ToUpperInvariant(), B(c.Color), Formatting.Isk(mine.Sum(Val)),
                $"{mine.Count} STACKS · TOP: {(top != null ? top.Name.ToUpperInvariant() : "—")}");
        }).ToList();

        var regAll = snap.Assets.GroupBy(a => StationRegion(a.StationId))
            .Select(g => (name: g.Key, v: g.Sum(Val))).OrderByDescending(x => x.v).ToList();
        var maxReg = regAll.Count > 0 ? regAll.Max(x => x.v) : 1;
        RegionBars = regAll.Select(x => new BarVm(x.name.ToUpperInvariant(), Formatting.Isk(x.v), B("#cfd8e0"),
            maxReg > 0 ? x.v / maxReg : 0, B("#4fc3f7"))).ToList();

        TopRows = snap.Assets.OrderByDescending(Val).Take(8).Select((a, i) => new TopRowVm(
            (i + 1).ToString("00"), a.Name, charById[a.CharId].Name, B(charById[a.CharId].Color),
            StationName(a.StationId), Formatting.Isk(Val(a)))).ToList();

        // ---- wallet ----
        var walletTotal = snap.Characters.Sum(c => c.WalletBalance);
        WalletTotalText = Formatting.Isk(walletTotal);
        GrandTotalText = Formatting.Isk(walletTotal + snap.Assets.Sum(Val));
        WalletCards = snap.Characters.Select(c => new WalletCardVm(
            c.Name.ToUpperInvariant(), B(c.Color), Formatting.Isk(c.WalletBalance),
            Formatting.Signed(c.WalletDelta30d), c.WalletDelta30d >= 0 ? B("#8bd450") : B("#c0533f"))).ToList();

        var wchips = new List<ChipVm>
        {
            new("All characters", "", B("#6b7887"), _walletChar == null, new RelayCommand(() => { _walletChar = null; Recompute(); })),
        };
        wchips.AddRange(snap.Characters.Select(c => new ChipVm(c.Name, "", B(c.Color), _walletChar == c.Id,
            new RelayCommand(() => { _walletChar = c.Id; Recompute(); }))));
        WalletChips = wchips;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var wj = snap.Journal.Where(j => (_walletChar == null || j.CharId == _walletChar) && charById.ContainsKey(j.CharId)).ToList();
        var wj30 = wj.Where(j => j.Ts >= cutoff).ToList();

        static string TypeLabel(JournalEntry j) => j.RefType switch
        {
            "bounty_prizes" => "BOUNTY PRIZES",
            "brokers_fee" or "transaction_tax" => "FEES + TAX",
            "contract_deposit" or "contract_price" or "contract_reward" or "contract_collateral" => "CONTRACTS",
            "market_escrow" => "ESCROW RELEASED",
            "insurance" => "INSURANCE",
            "player_donation" or "corporation_account_withdrawal" => "TRANSFERS",
            "market_transaction" => "MARKET SELLS", // split below by sign
            _ => j.RefType.Replace('_', ' ').ToUpperInvariant(),
        };
        string FlowKey(JournalEntry j) => j.RefType == "market_transaction" ? (j.Amount >= 0 ? "MARKET SELLS" : "MARKET BUYS") : TypeLabel(j);

        var flows = wj30.GroupBy(FlowKey).Select(g => (name: g.Key, v: g.Sum(x => x.Amount)))
            .OrderByDescending(x => x.v).ToList();
        var maxFlow = flows.Count > 0 ? Math.Max(flows.Max(f => Math.Abs(f.v)), 1) : 1;
        FlowBars = flows.Select(f => new BarVm(f.name, Formatting.Signed(f.v),
            f.v >= 0 ? B("#8bd450") : B("#c0533f"), Math.Abs(f.v) / maxFlow, f.v >= 0 ? B("#8bd450") : B("#c0533f"))).ToList();
        var net30 = flows.Sum(f => f.v);
        Net30Text = Formatting.Signed(net30);
        Net30Brush = net30 >= 0 ? B("#8bd450") : B("#c0533f");

        double spaceIn = 0, spaceOut = 0;
        var stFlows = new Dictionary<long, (double inV, double outV)>();
        foreach (var j in wj30)
        {
            if (j.StationId is not { } sid) { if (j.Amount >= 0) spaceIn += j.Amount; else spaceOut += j.Amount; continue; }
            var e = stFlows.GetValueOrDefault(sid);
            if (j.Amount >= 0) e.inV += j.Amount; else e.outV += j.Amount;
            stFlows[sid] = e;
        }
        StationFlows = stFlows.Select(kv =>
        {
            var net = kv.Value.inV + kv.Value.outV;
            return (net, vm: new StationFlowVm(StationName(kv.Key),
                kv.Value.inV != 0 ? "+" + Formatting.Isk(kv.Value.inV) : "—",
                kv.Value.outV != 0 ? "−" + Formatting.Isk(-kv.Value.outV) : "—",
                Formatting.Signed(net), net >= 0 ? B("#8bd450") : B("#c0533f")));
        }).OrderByDescending(x => Math.Abs(x.net)).Select(x => x.vm).ToList();
        SpaceFlowText = Formatting.Signed(spaceIn + spaceOut) + " NET";

        JournalRows = wj.OrderByDescending(j => j.Ts).Take(30).Select(j => new JournalRowVm(
            j.Ts.ToLocalTime().ToString("MM-dd HH:mm"),
            charById[j.CharId].Name, B(charById[j.CharId].Color), j.Desc,
            j.StationId is { } sid2 ? StationName(sid2) : "— in space",
            j.RefType, Formatting.Signed(j.Amount), j.Amount >= 0 ? B("#8bd450") : B("#c0533f"))).ToList();
        JournalScopeText = _walletChar is { } wc && charById.TryGetValue(wc, out var wcc) ? wcc.Name.ToUpperInvariant() : "ALL CHARACTERS";

        // ---- locations ----
        LocCards = snap.Characters.Select(c =>
        {
            var nearest = snap.Stations.Values
                .Select(st => (st, v: snap.Assets.Where(a => a.StationId == st.Id).Sum(Val), j: st.Jumps.GetValueOrDefault(c.Id, -1)))
                .Where(x => x.v > 0)
                .OrderBy(x => x.j < 0 ? int.MaxValue : x.j)
                .Take(4)
                .Select(x => new NearestVm(
                    x.j == 0 ? "HERE" : x.j < 0 ? "—" : $"{x.j} J",
                    x.j == 0 ? B("#8bd450") : x.j >= 0 && x.j <= 10 ? B("#cfd8e0") : B("#6b7887"),
                    x.st.Name, Formatting.Isk(x.v)))
                .ToList();
            var sec = c.SecStatus ?? 0;
            var status = c.Docked ? $"DOCKED · {c.StationName ?? c.SystemName?.ToUpperInvariant() ?? ""}" : "IN SPACE · ON GRID";
            return new LocCardVm(c.Name,
                string.Join("", c.Name.Split(' ').Where(w => w.Length > 0).Select(w => w[0])),
                B(c.Color), status,
                c.SystemName ?? "—", sec.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                sec >= 0.5 ? B("#8bd450") : sec > 0 ? B("#ffb300") : B("#c0533f"),
                c.RegionName ?? "—", c.ShipName ?? "—",
                Formatting.Isk(snap.Assets.Where(a => a.CharId == c.Id).Sum(Val)) + " ISK", nearest);
        }).ToList();

        // ---- auth ----
        AuthRows = snap.Characters.Select(c => new AuthRowVm(
            c.Name, B(c.Color),
            $"{c.ScopesGranted.Count}/{EsiScopes.Required.Length} GRANTED",
            Formatting.In(c.TokenExpiresAt), Formatting.Ago(c.LastSyncAt),
            new AsyncRelayCommand(() => _sync.SyncOne(c.Id)),
            new RelayCommand(() => _sync.Unlink(c.Id)))).ToList();

        // ---- settings ----
        CacheSizeText = $"Local cache · {snap.CacheSizeBytes / 1024.0 / 1024.0:0.0} MB";
    }

    private void SyncOptions(ObservableCollection<OptionVm> target, List<OptionVm> fresh, ref int index, string indexProp)
    {
        if (target.Select(o => o.Label).SequenceEqual(fresh.Select(o => o.Label))) return;
        _loadingUi = true;
        target.Clear();
        foreach (var o in fresh) target.Add(o);
        index = 0;
        OnPropertyChanged(indexProp);
        _loadingUi = false;
    }
}
