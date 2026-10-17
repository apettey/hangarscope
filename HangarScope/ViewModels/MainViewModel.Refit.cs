using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HangarScope.Models;
using HangarScope.Services;

namespace HangarScope.ViewModels;

// REFIT screen: pick ships, set one target fit, and show what to strip, swap, pick up and buy.
public sealed partial class MainViewModel
{
    private bool _refitLoaded;
    private RefitFile _refitFile = new();
    private string _refitShipFilter = "";
    private readonly HashSet<long> _refitSelected = new();
    private RefitTarget _refitTarget = new();
    private bool _useSwaps = true, _useStock = true, _allowContracts = true, _includeDrones = true, _includeCharges, _includeCargo;
    private int _newHulls;
    private long _newHullChar;
    private RefitPlan? _lastPlan;
    private Func<long, string> _typeName = id => $"Type {id}";
    private Snapshot? _stockSnap;
    private List<StockStack> _stock = new();
    private Func<Task>? _pendingWrite;

    /// <summary>Set by RefitView: puts text on the system clipboard.</summary>
    public Func<string, Task>? CopyToClipboard { get; set; }

    [ObservableProperty] private bool _isRefit;
    [ObservableProperty] private bool _refitPlanMode = true;
    [ObservableProperty] private bool _refitFittingsMode;
    [ObservableProperty] private List<RefitShipVm> _refitShips = new();
    [ObservableProperty] private string _refitShipsHeader = "SHIPS";
    [ObservableProperty] private List<FitLibVm> _fitLibrary = new();
    [ObservableProperty] private string _fitLibraryHeader = "";
    [ObservableProperty] private List<ActionVm> _copyShipOptions = new();
    [ObservableProperty] private string _targetTitle = "";
    [ObservableProperty] private string _targetSub = "";
    [ObservableProperty] private string _newHullsText = "0";
    [ObservableProperty] private string _newHullWhereText = "";
    [ObservableProperty] private List<SummaryCardVm> _refitCards = new();
    [ObservableProperty] private List<MatrixColVm> _matrixCols = new();
    [ObservableProperty] private List<MatrixRowVm> _matrixRows = new();
    [ObservableProperty] private bool _matrixEmpty = true;
    [ObservableProperty] private List<WorkOrderVm> _workOrders = new();
    [ObservableProperty] private List<ListRowVm> _pickupRows = new();
    [ObservableProperty] private List<ListRowVm> _buyRows = new();
    [ObservableProperty] private List<ListRowVm> _leftoverRows = new();
    [ObservableProperty] private string _buyTotalText = "";
    [ObservableProperty] private string _buyTotalSub = "";
    [ObservableProperty] private string _shoppingHeader = "";
    [ObservableProperty] private string _refitFooterText = "";
    [ObservableProperty] private string _refitStatusText = "";
    [ObservableProperty] private bool _isEftOpen;
    [ObservableProperty] private string _eftText = "";
    [ObservableProperty] private bool _hasPendingWrite;
    [ObservableProperty] private string _pendingWriteText = "";
    [ObservableProperty] private List<SavedPlanVm> _savedPlans = new();
    [ObservableProperty] private string _planNameInput = "";

    public ObservableCollection<OptionVm> NewHullPilotOptions { get; } = new();

    public string RefitShipFilter
    {
        get => _refitShipFilter;
        set { if (SetProperty(ref _refitShipFilter, value)) RefitChanged(); }
    }

    public bool UseSwaps { get => _useSwaps; set { if (SetProperty(ref _useSwaps, value)) RefitChanged(); } }
    public bool UseStock { get => _useStock; set { if (SetProperty(ref _useStock, value)) RefitChanged(); } }
    public bool AllowContracts { get => _allowContracts; set { if (SetProperty(ref _allowContracts, value)) RefitChanged(); } }
    public bool IncludeDrones { get => _includeDrones; set { if (SetProperty(ref _includeDrones, value)) RefitChanged(); } }
    public bool IncludeCharges { get => _includeCharges; set { if (SetProperty(ref _includeCharges, value)) RefitChanged(); } }
    public bool IncludeCargo { get => _includeCargo; set { if (SetProperty(ref _includeCargo, value)) RefitChanged(); } }

    public string TargetName
    {
        get => _refitTarget.Name;
        set { if (_refitTarget.Name == value) return; _refitTarget.Name = value; OnPropertyChanged(); RefitChanged(); }
    }

    private int _newHullPilotIndex;
    public int NewHullPilotIndex
    {
        get => _newHullPilotIndex;
        set
        {
            if (!SetProperty(ref _newHullPilotIndex, value) || _loadingUi) return;
            _newHullChar = (long)(NewHullPilotOptions.ElementAtOrDefault(Math.Max(0, value))?.Value ?? 0L);
            RefitChanged();
        }
    }

    private void RefitChanged()
    {
        if (_loadingUi) return;
        PersistRefit();
        Invalidate("refit");
    }

    // ------------------------------------------------------------------ commands

    [RelayCommand] private void ShowPlan() { RefitPlanMode = true; RefitFittingsMode = false; }
    [RelayCommand] private void ShowFittings() { RefitPlanMode = false; RefitFittingsMode = true; }

    [RelayCommand] private void NewHullsMinus() { _newHulls = Math.Max(0, _newHulls - 1); RefitChanged(); }
    [RelayCommand] private void NewHullsPlus() { _newHulls = Math.Min(20, _newHulls + 1); RefitChanged(); }

    [RelayCommand]
    private void ClearTarget()
    {
        _refitTarget = new RefitTarget { ShipTypeId = _refitTarget.ShipTypeId };
        OnPropertyChanged(nameof(TargetName));
        RefitChanged();
    }

    [RelayCommand]
    private void UnionTarget()
    {
        var ships = SelectedShips();
        if (ships.Count == 0) { RefitStatusText = "SELECT SHIPS FIRST"; return; }
        var max = new Dictionary<(string g, long t), long>();
        foreach (var s in ships)
            foreach (var l in RefitPlanner.ToLines(s.Items, _snap.Types))
                max[(l.Group, l.TypeId)] = Math.Max(max.GetValueOrDefault((l.Group, l.TypeId)), l.Qty);
        SetTarget(ships[0].TypeId, "Union of " + ships.Count + " ships",
            max.Select(kv => new TargetLine { Group = kv.Key.g, TypeId = kv.Key.t, Qty = kv.Value }).ToList());
    }

    private void CopyShip(long itemId)
    {
        var ship = _snap.Ships.FirstOrDefault(s => s.ItemId == itemId);
        if (ship == null) return;
        SetTarget(ship.TypeId, ship.Name ?? _typeName(ship.TypeId), RefitPlanner.ToLines(ship.Items, _snap.Types));
    }

    [RelayCommand] private void OpenEft() { IsEftOpen = true; EftText = ""; }
    [RelayCommand] private void CancelEft() { IsEftOpen = false; }

    [RelayCommand]
    private async Task ImportEft()
    {
        var byName = NameIndex();
        long? Resolve(string n) => byName.TryGetValue(n, out var id) ? id : null;
        var r = EftFormat.Parse(EftText, Resolve, _snap.Types);
        if (r.Error != null) { RefitStatusText = r.Error.ToUpperInvariant(); return; }
        if (r.Unresolved.Count > 0)
        {
            RefitStatusText = "LOOKING UP " + r.Unresolved.Count + " NAMES…";
            foreach (var (n, id) in await _sync.ResolveTypeNames(r.Unresolved)) byName[n] = id;
            r = EftFormat.Parse(EftText, Resolve, _snap.Types);
        }
        if (r.HullTypeId == null) { RefitStatusText = $"UNKNOWN HULL: {r.HullName.ToUpperInvariant()}"; return; }
        SetTarget(r.HullTypeId.Value, r.Name, r.Lines);
        IsEftOpen = false;
        RefitStatusText = r.Unresolved.Count > 0 ? "SKIPPED UNKNOWN: " + string.Join(", ", r.Unresolved).ToUpperInvariant() : "EFT IMPORTED";
        _ = _sync.EnsureTypes(r.Lines.Select(l => l.TypeId).Append(r.HullTypeId.Value));
    }

    [RelayCommand]
    private async Task CopyTargetEft()
    {
        if (CopyToClipboard == null || _refitTarget.Lines.Count == 0) return;
        var hull = TargetHull();
        await CopyToClipboard(EftFormat.Export(new RefitTarget { ShipTypeId = hull, Name = _refitTarget.Name, Lines = _refitTarget.Lines },
            _typeName, _snap.Types.GetValueOrDefault(hull)?.Slots));
        RefitStatusText = "TARGET COPIED AS EFT";
    }

    [RelayCommand]
    private async Task CopyMultibuy()
    {
        if (CopyToClipboard == null || _lastPlan == null || _lastPlan.Buys.Count == 0) return;
        await CopyToClipboard(EftFormat.Multibuy(_lastPlan.Buys, _typeName));
        RefitStatusText = "SHOPPING LIST COPIED · PASTE INTO MULTIBUY";
    }

    [RelayCommand]
    private void AskSaveTarget()
    {
        var hull = TargetHull();
        if (hull == 0 || _refitTarget.Lines.Count == 0) { RefitStatusText = "SET A TARGET FIT FIRST"; return; }
        var items = RefitPlanner.ToFitItems(_refitTarget.Lines);
        var hash = FitMath.Hash(hull, items);
        var have = _snap.Fittings.Where(f => FitMath.Hash(f.ShipTypeId, f.Items) == hash).Select(f => f.CharId).ToHashSet();
        var missing = _snap.Characters.Where(c => !have.Contains(c.Id)).ToList();
        if (missing.Count == 0) { RefitStatusText = "EVERY PILOT ALREADY HAS THIS FIT"; return; }
        var name = string.IsNullOrWhiteSpace(_refitTarget.Name) ? _typeName(hull) + " refit" : _refitTarget.Name;
        AskWrite($"SAVE FITTING “{name}” ({_typeName(hull)}, {items.Count} ITEMS) IN GAME FOR {missing.Count} PILOT{(missing.Count == 1 ? "" : "S")}: " +
                 string.Join(", ", missing.Select(c => c.Name)).ToUpperInvariant(),
            () => WriteFitting(missing.Select(c => c.Id).ToList(), new SavedFitting { Name = name, ShipTypeId = hull, Items = items }));
    }

    [RelayCommand]
    private async Task ConfirmWrite()
    {
        var w = _pendingWrite;
        _pendingWrite = null;
        HasPendingWrite = false;
        if (w != null) await w();
    }

    [RelayCommand] private void CancelWrite() { _pendingWrite = null; HasPendingWrite = false; RefitStatusText = "NOTHING WRITTEN"; }

    [RelayCommand]
    private void SavePlan()
    {
        var name = string.IsNullOrWhiteSpace(PlanNameInput) ? (_refitTarget.Name.Length > 0 ? _refitTarget.Name : "Refit plan") : PlanNameInput.Trim();
        var plan = CaptureRefit();
        plan.Name = name;
        _refitFile.Plans.RemoveAll(p => p.Name == name);
        _refitFile.Plans.Insert(0, plan);
        PlanNameInput = "";
        PersistRefit();
        RefitStatusText = $"PLAN “{name.ToUpperInvariant()}” SAVED";
        Invalidate("refit");
    }

    private void AskWrite(string text, Func<Task> write)
    {
        if (_snap.Demo)
        {
            RefitStatusText = "DEMO DATA · LINK A CHARACTER TO WRITE IN-GAME FITTINGS";
            return;
        }
        _pendingWrite = write;
        PendingWriteText = text;
        HasPendingWrite = true;
    }

    private async Task WriteFitting(List<long> charIds, SavedFitting fit)
    {
        RefitStatusText = "SAVING FITTING…";
        var results = await _sync.SaveFitting(charIds, fit);
        var ok = results.Count(r => r.ok);
        var failed = results.Where(r => !r.ok).Select(r => r.message).ToList();
        RefitStatusText = $"SAVED ON {ok} OF {results.Count} PILOTS" + (failed.Count > 0 ? " · " + string.Join(" · ", failed).ToUpperInvariant() : "");
    }

    private async Task DeleteFits(List<SavedFitting> fits)
    {
        RefitStatusText = "DELETING…";
        var ok = 0;
        var failed = new List<string>();
        foreach (var f in fits)
        {
            var (good, msg) = await _sync.DeleteFitting(f.CharId, f.FittingId);
            if (good) ok++; else failed.Add(msg);
        }
        RefitStatusText = $"DELETED {ok} OF {fits.Count} COPIES" + (failed.Count > 0 ? " · " + string.Join(" · ", failed).ToUpperInvariant() : "");
    }

    // ------------------------------------------------------------------ state

    private void SetTarget(long hull, string name, List<TargetLine> lines)
    {
        _refitTarget = new RefitTarget { ShipTypeId = hull, Name = name, Lines = lines.Where(l => l.Qty > 0).ToList() };
        OnPropertyChanged(nameof(TargetName));
        RefitChanged();
    }

    private void StepTarget(string group, long typeId, int delta)
    {
        var line = _refitTarget.Lines.FirstOrDefault(l => l.Group == group && l.TypeId == typeId);
        if (line == null)
        {
            if (delta <= 0) return;
            _refitTarget.Lines.Add(line = new TargetLine { Group = group, TypeId = typeId });
            if (_refitTarget.ShipTypeId == 0) _refitTarget.ShipTypeId = TargetHull();
        }
        line.Qty = Math.Max(0, line.Qty + delta);
        if (line.Qty == 0) _refitTarget.Lines.Remove(line);
        RefitChanged();
    }

    private long TargetHull() => _refitTarget.ShipTypeId != 0 ? _refitTarget.ShipTypeId : SelectedShips().FirstOrDefault()?.TypeId ?? 0;

    private List<ShipInstance> SelectedShips() =>
        _snap.Ships.Where(s => _refitSelected.Contains(s.ItemId))
            .OrderBy(s => s.TypeId).ThenBy(s => s.CharId).ThenBy(s => s.ItemId).ToList();

    private Dictionary<string, long> NameIndex()
    {
        var d = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, t) in _snap.Types) d.TryAdd(t.Name, id);
        foreach (var (id, n) in _snap.TypeNames) d.TryAdd(n, id);
        return d;
    }

    private SavedRefitPlan CaptureRefit() => new()
    {
        SavedAt = DateTimeOffset.UtcNow, ShipFilter = _refitShipFilter, ShipItemIds = _refitSelected.ToList(),
        Target = new RefitTarget
        {
            ShipTypeId = _refitTarget.ShipTypeId, Name = _refitTarget.Name,
            Lines = _refitTarget.Lines.Select(l => new TargetLine { Group = l.Group, TypeId = l.TypeId, Qty = l.Qty }).ToList(),
        },
        UseSwaps = _useSwaps, UseStock = _useStock, AllowContracts = _allowContracts,
        IncludeDrones = _includeDrones, IncludeCharges = _includeCharges, IncludeCargo = _includeCargo,
        NewHulls = _newHulls, NewHullCharId = _newHullChar,
    };

    private void ApplyRefit(SavedRefitPlan p)
    {
        _loadingUi = true;
        _refitShipFilter = p.ShipFilter; OnPropertyChanged(nameof(RefitShipFilter));
        _refitSelected.Clear();
        foreach (var id in p.ShipItemIds) _refitSelected.Add(id);
        _refitTarget = new RefitTarget
        {
            ShipTypeId = p.Target.ShipTypeId, Name = p.Target.Name,
            Lines = p.Target.Lines.Select(l => new TargetLine { Group = l.Group, TypeId = l.TypeId, Qty = l.Qty }).ToList(),
        };
        OnPropertyChanged(nameof(TargetName));
        _useSwaps = p.UseSwaps; _useStock = p.UseStock; _allowContracts = p.AllowContracts;
        _includeDrones = p.IncludeDrones; _includeCharges = p.IncludeCharges; _includeCargo = p.IncludeCargo;
        foreach (var n in new[] { nameof(UseSwaps), nameof(UseStock), nameof(AllowContracts), nameof(IncludeDrones), nameof(IncludeCharges), nameof(IncludeCargo) })
            OnPropertyChanged(n);
        _newHulls = p.NewHulls; _newHullChar = p.NewHullCharId;
        _loadingUi = false;
    }

    private void PersistRefit()
    {
        if (!_refitLoaded) return;
        _refitFile.Current = CaptureRefit();
        _sync.SaveRefits(_refitFile);
    }

    private void EnsureRefitLoaded()
    {
        if (_refitLoaded) return;
        _refitLoaded = true;
        _refitFile = _sync.LoadRefits();
        if (_refitFile.Current != null) ApplyRefit(_refitFile.Current);
        else if (_snap.Demo) ApplyRefit(DemoData.RefitDefaults());
    }

    // ------------------------------------------------------------------ recompute

    private static readonly IBrush TagSwap = B("#8bd450"), TagSwapB = B("#3c5a26");
    private static readonly IBrush TagPick = B("#ffb300"), TagPickB = B("#6b5214");
    private static readonly IBrush TagBuy = B("#4fc3f7"), TagBuyB = B("#1f5670");
    private static readonly IBrush TagTrade = B("#b48ead"), TagTradeB = B("#5a4660");
    private static readonly IBrush TagRig = B("#c0533f"), TagRigB = B("#5e2b22");
    private static readonly IBrush TagSur = B("#8fa0b0"), TagSurB = B("#3a4a5c");

    private static TagVm Tag(FitSource s) => s switch
    {
        FitSource.Swap => new TagVm("SWAP", TagSwap, TagSwapB),
        FitSource.Contract => new TagVm("CONTRACT", TagTrade, TagTradeB),
        FitSource.Pickup => new TagVm("PICKUP", TagPick, TagPickB),
        _ => new TagVm("BUY", TagBuy, TagBuyB),
    };

    private int StationJumps(long from, long to)
    {
        if (from == to) return 0;
        var a = _snap.Stations.GetValueOrDefault(from);
        var b = _snap.Stations.GetValueOrDefault(to);
        if (a == null || b == null) return -1;
        if (a.SystemId != 0 && a.SystemId == b.SystemId) return 0;
        // route lengths are known from each character's current system: use a character sitting where the ships are
        foreach (var (charId, j) in b.Jumps)
            if (j == 0 && a.Jumps.TryGetValue(charId, out var ja)) return ja;
        return -1;
    }

    private static string JumpsLabel(int j) => j == 0 ? "HERE" : j < 0 ? "HAUL IN" : $"{j} J";

    private void ComputeRefit()
    {
        EnsureRefitLoaded();
        var snap = _snap;
        var chars = snap.Characters.ToDictionary(c => c.Id);
        string CharName(long id) => chars.TryGetValue(id, out var c) ? c.Name : $"Character {id}";
        IBrush CharColor(long id) => chars.TryGetValue(id, out var c) ? B(c.Color) : B("#6b7887");
        _typeName = id => snap.Types.TryGetValue(id, out var t) ? t.Name : snap.TypeNames.TryGetValue(id, out var n) ? n : $"Type {id}";
        var tn = _typeName;
        var hub = PriceHubDef.ByKey(_sync.Settings.PriceHub);

        // ---- rail: ships
        var selected = SelectedShips();
        var keyOf = new Dictionary<long, string>();
        for (var i = 0; i < selected.Count; i++) keyOf[selected[i].ItemId] = i < 26 ? ((char)('A' + i)).ToString() : "S" + (i + 1);
        var q = _refitShipFilter.Trim().ToLowerInvariant();
        RefitShips = snap.Ships
            .Where(s => q.Length == 0 || tn(s.TypeId).ToLowerInvariant().Contains(q) || (s.Name ?? "").ToLowerInvariant().Contains(q)
                        || CharName(s.CharId).ToLowerInvariant().Contains(q))
            .OrderByDescending(s => _refitSelected.Contains(s.ItemId)).ThenBy(s => tn(s.TypeId)).ThenBy(s => CharName(s.CharId))
            .Select(s =>
            {
                var sel = keyOf.TryGetValue(s.ItemId, out var k);
                var mods = s.Items.Where(i => FitMath.IsFittedFlag(i.Flag) && !RefitPlanner.IsCharge(snap.Types, i.TypeId)).Sum(i => i.Qty);
                return new RefitShipVm((sel ? k + " · " : "") + (s.Name ?? tn(s.TypeId)), tn(s.TypeId).ToUpperInvariant(),
                    CharName(s.CharId), CharColor(s.CharId), StationName(s.StationId), $"{mods} FITTED",
                    sel, new RelayCommand(() =>
                    {
                        if (!_refitSelected.Remove(s.ItemId)) _refitSelected.Add(s.ItemId);
                        RefitChanged();
                    }));
            }).ToList();
        RefitShipsHeader = $"SHIPS · {selected.Count} OF {snap.Ships.Count} SELECTED";
        CopyShipOptions = selected.Select(s => new ActionVm($"{keyOf[s.ItemId]} · {s.Name ?? tn(s.TypeId)}",
            CharName(s.CharId), new RelayCommand(() => CopyShip(s.ItemId)))).ToList();

        // ---- rail: new hulls pilot picker
        SyncOptions(NewHullPilotOptions, snap.Characters.Select(c => new OptionVm(c.Name, c.Id)).ToList(), ref _newHullPilotIndex, nameof(NewHullPilotIndex));
        var pilotIdx = NewHullPilotOptions.ToList().FindIndex(o => (long)o.Value! == _newHullChar);
        if (pilotIdx < 0 && NewHullPilotOptions.Count > 0) { pilotIdx = 0; _newHullChar = (long)NewHullPilotOptions[0].Value!; }
        if (pilotIdx != _newHullPilotIndex) { _loadingUi = true; NewHullPilotIndex = pilotIdx; _loadingUi = false; }
        NewHullsText = _newHulls.ToString();

        // ---- fittings library (identical fits on several pilots are one row)
        var fitGroups = snap.Fittings.GroupBy(f => FitMath.Hash(f.ShipTypeId, f.Items)).ToList();
        FitLibrary = fitGroups
            .Select(g => (g, first: g.First()))
            .Where(x => q.Length == 0 || x.first.Name.ToLowerInvariant().Contains(q) || tn(x.first.ShipTypeId).ToLowerInvariant().Contains(q))
            .OrderBy(x => tn(x.first.ShipTypeId)).ThenBy(x => x.first.Name)
            .Select(x =>
            {
                var have = x.g.Select(f => f.CharId).ToHashSet();
                var missing = snap.Characters.Where(c => !have.Contains(c.Id)).ToList();
                var names = x.g.Select(f => f.Name).Distinct().ToList();
                var fit = x.first;
                var mods = fit.Items.Where(i => FitMath.SlotGroup(i.Flag) is "HIGH" or "MED" or "LOW" or "RIG" or "SUBSYSTEM").Sum(i => i.Qty);
                return new FitLibVm
                {
                    Name = names[0] + (names.Count > 1 ? $" (+{names.Count - 1} NAMES)" : ""),
                    HullName = tn(fit.ShipTypeId),
                    ModulesText = $"{mods} MODULES · {fit.Items.Count} LINES",
                    Pilots = snap.Characters.Select(c => new PilotDotVm(c.Name, B(c.Color), have.Contains(c.Id))).ToList(),
                    SavedOnText = have.Count == 1 ? $"SAVED ON {CharName(have.First()).ToUpperInvariant()} ONLY" : $"SAVED ON {have.Count} OF {snap.Characters.Count} PILOTS",
                    MissingText = missing.Count > 0 ? $"MISSING ON {missing.Count} PILOT{(missing.Count == 1 ? "" : "S")}" : "",
                    CanPush = missing.Count > 0,
                    Use = new RelayCommand(() => { SetTarget(fit.ShipTypeId, fit.Name, RefitPlanner.ToLines(fit.Items, snap.Types)); ShowPlan(); }),
                    AskPush = new RelayCommand(() =>
                    {
                        if (missing.Count == 0) return;
                        AskWrite($"SAVE FITTING “{fit.Name}” IN GAME FOR {missing.Count} PILOT{(missing.Count == 1 ? "" : "S")}: " +
                                 string.Join(", ", missing.Select(c => c.Name)).ToUpperInvariant(),
                            () => WriteFitting(missing.Select(c => c.Id).ToList(), fit));
                    }),
                    AskDelete = new RelayCommand(() =>
                        AskWrite($"DELETE FITTING “{fit.Name}” IN GAME FROM {x.g.Count()} PILOT{(x.g.Count() == 1 ? "" : "S")}: " +
                                 string.Join(", ", x.g.Select(f => CharName(f.CharId))).ToUpperInvariant() + " · THIS CANNOT BE UNDONE",
                            () => DeleteFits(x.g.ToList()))),
                };
            }).ToList();
        FitLibraryHeader = $"IN-GAME FITTINGS · {fitGroups.Count} FITS · {snap.Fittings.Count} COPIES ACROSS {snap.Characters.Count} PILOTS";

        SavedPlans = _refitFile.Plans.Select(p => new SavedPlanVm(p.Name,
            $"{p.ShipItemIds.Count} SHIPS · {p.NewHulls} NEW · {Formatting.Ago(p.SavedAt).ToUpperInvariant()}",
            new RelayCommand(() => { ApplyRefit(p); RefitStatusText = $"PLAN “{p.Name.ToUpperInvariant()}” LOADED"; RefitChanged(); }),
            new RelayCommand(() => { _refitFile.Plans.Remove(p); PersistRefit(); Invalidate("refit"); }))).ToList();

        // ---- plan
        var hull = TargetHull();
        TargetTitle = hull == 0 ? "NO TARGET YET" : tn(hull).ToUpperInvariant();
        TargetSub = _refitTarget.Lines.Count == 0 ? "Pick an in-game fit, paste EFT or copy a ship"
            : $"{_refitTarget.Lines.Where(l => RefitPlanner.Compared(l.Group, Options())).Sum(l => l.Qty)} items in the target";
        var newSt = selected.FirstOrDefault()?.StationId ?? 0;
        NewHullWhereText = _newHulls == 0 ? "No new hulls" : $"At {(newSt != 0 ? StationName(newSt) : "the ships' station")}";

        if (_stockSnap != snap)
        {
            _stock = RefitPlanner.BuildStock(snap.Assets, snap.Ships);
            _stockSnap = snap;
        }
        var planShips = selected.Select(s => new PlanShip
        {
            Key = keyOf[s.ItemId], ItemId = s.ItemId, TypeId = s.TypeId, Name = s.Name ?? tn(s.TypeId),
            CharId = s.CharId, StationId = s.StationId, Items = s.Items,
        }).ToList();
        var opts = Options();
        opts.NewHulls = new NewHullSpec { Count = planShips.Count > 0 || hull != 0 ? _newHulls : 0, CharId = _newHullChar, StationId = newSt };
        var plan = RefitPlanner.Plan(planShips, new RefitTarget { ShipTypeId = hull, Name = _refitTarget.Name, Lines = _refitTarget.Lines }, _stock, opts);
        _lastPlan = plan;
        var shipByKey = plan.Ships.ToDictionary(s => s.Key);
        string ShipLabel(string key) => shipByKey.TryGetValue(key, out var s) ? $"{key} · {s.Name}" : key;

        // ---- cards
        var existing = plan.Ships.Count(s => !s.IsNew);
        RefitCards = new List<SummaryCardVm>
        {
            new("SHIPS IN PLAN", plan.Ships.Count.ToString(), $"{existing} REFIT · {plan.Ships.Count - existing} NEW"),
            new("MODULES TO STRIP", plan.StripCount.ToString(), plan.RigsDestroyed > 0 ? $"{plan.RigsDestroyed} RIG{(plan.RigsDestroyed > 1 ? "S" : "")} DESTROYED" : "NO RIGS LOST"),
            new("MOVED BETWEEN SHIPS", plan.MovedBetweenShips.ToString(), "SWAP + CONTRACT"),
            new("PICKUPS", plan.PickupCount.ToString(), $"{plan.Pickups.Select(p => p.Stack.StationId).Distinct().Count()} LOCATIONS"),
            new("TO BUY", Formatting.Isk(plan.BuyTotal), (plan.HullsToBuy > 0 ? $"{plan.HullsToBuy} HULL{(plan.HullsToBuy > 1 ? "S" : "")} + " : "") + $"{plan.ItemsToBuy} ITEMS · {hub.Label} {_priceSide.ToUpperInvariant()}"),
        };

        // ---- matrix
        MatrixCols = plan.Ships.Select(s => new MatrixColVm(s.Key, s.IsNew ? B("#6b7887") : CharColor(s.CharId),
            $"{s.Name} · {(s.IsNew ? "new hull for " : "")}{CharName(s.CharId)}")).ToList();
        var checks = plan.SlotChecks.ToLookup(c => c.Group);
        var rows = new List<MatrixRowVm>();
        foreach (var g in RefitPlanner.GroupOrder)
        {
            var groupRows = plan.Rows.Where(r => r.Group == g).ToList();
            var cks = checks[g].ToList();
            if (groupRows.Count == 0 && cks.All(c => c.Used == 0)) continue;
            var (statusText, statusBrush) = SlotStatus(cks, hull, tn);
            rows.Add(new MatrixRowVm(true, g + (g is "DRONES" or "FIGHTERS" ? " · BAY" : g == "CHARGES" ? " · LOADED + CARGO" : ""),
                statusText, statusBrush, new List<MatrixCellVm>(), "", null, null, new List<TagVm>(), ""));
            foreach (var r in groupRows)
            {
                var cells = plan.Ships.Select(s =>
                {
                    var c = r.Current.GetValueOrDefault(s.Key);
                    var d = r.Target - c;
                    return d == 0 ? new MatrixCellVm(c > 0 ? "✓ " + c : "·", B("#6b7887"))
                        : d < 0 ? new MatrixCellVm($"{c} −{-d}", B("#ffb300"))
                        : new MatrixCellVm($"{c} +{d}", B("#4fc3f7"));
                }).ToList();
                var tags = r.Sources.OrderBy(x => x).Select(Tag).ToList();
                if (r.Destroyed) tags.Add(new TagVm("DESTROYED", TagRig, TagRigB));
                if (r.LeftOver > 0) tags.Add(new TagVm("LEFT OVER", TagSur, TagSurB));
                var (grp, tid) = (r.Group, r.TypeId);
                rows.Add(new MatrixRowVm(false, tn(r.TypeId), "", B("#6b7887"), cells, r.Target.ToString(),
                    new RelayCommand(() => StepTarget(grp, tid, -1)), new RelayCommand(() => StepTarget(grp, tid, +1)),
                    tags, r.BuyCost > 0 ? Formatting.Isk(r.BuyCost) : "—"));
            }
        }
        MatrixRows = rows;
        MatrixEmpty = plan.Ships.Count == 0;

        // ---- work orders
        string FromText(FitLine f, PlanShip to) => f.Source switch
        {
            FitSource.Swap => $"from {ShipLabel(f.FromShipKey!)}",
            FitSource.Contract => $"from {ShipLabel(f.FromShipKey!)} · contract {CharName(f.FromCharId)} → {CharName(to.CharId)}",
            FitSource.Pickup => $"{f.Where} · {StationName(f.FromStationId)} · {JumpsLabel(f.Jumps)}" +
                                (f.NeedsContract ? $" · contract from {CharName(f.FromCharId)}" : ""),
            _ => $"buy at {hub.Label} · haul to {StationName(to.StationId)}",
        };
        WorkOrders = plan.Ships.Select(s =>
        {
            var o = plan.Orders[s.Key];
            var strip = o.Strip.Select(x => new OrderLineVm("−" + x.Qty, tn(x.TypeId),
                x.Destroyed ? new List<TagVm> { new("DESTROYED", TagRig, TagRigB) } : new List<TagVm>(), "")).ToList();
            var fit = o.Fit.Select(f =>
            {
                var tags = new List<TagVm> { Tag(f.Source) };
                if (f.NeedsContract) tags.Add(new TagVm("CONTRACT", TagTrade, TagTradeB));
                return new OrderLineVm("+" + f.Qty, tn(f.TypeId), tags, FromText(f, s));
            }).ToList();
            return new WorkOrderVm($"{s.Key} · {s.Name}".ToUpperInvariant(),
                (s.IsNew ? "NEW HULL · " : "") + CharName(s.CharId).ToUpperInvariant() + " · " + StationName(s.StationId).ToUpperInvariant(),
                s.IsNew ? B("#6b7887") : CharColor(s.CharId),
                strip, s.IsNew ? "New hull · nothing to strip" : "Nothing to strip",
                fit, "Already matches the target");
        }).ToList();

        // ---- lists
        PickupRows = plan.Pickups.Select(p => new ListRowVm(tn(p.TypeId),
            $"{p.Stack.Where} · {StationName(p.Stack.StationId)}",
            $"{CharName(p.Stack.CharId)} → ship {p.ToShipKey}" + (p.NeedsContract ? " · contract" : ""), CharColor(p.Stack.CharId),
            "× " + Formatting.Qty(p.Qty), JumpsLabel(p.Jumps), p.Jumps == 0 ? B("#8bd450") : B("#6b7887"),
            p.NeedsContract ? new List<TagVm> { new("CONTRACT", TagTrade, TagTradeB) } : new List<TagVm>())).ToList();
        var newPilot = CharName(_newHullChar);
        BuyRows = plan.Buys.Select(b => new ListRowVm(tn(b.TypeId),
            b.IsHull ? $"New ship{(b.Qty > 1 ? "s" : "")} for {newPilot}" : b.UnitPrice > 0 ? $"{Formatting.Isk(b.UnitPrice)} each" : "No market price",
            "", B("#6b7887"), "× " + Formatting.Qty(b.Qty), b.UnitPrice > 0 ? Formatting.Isk(b.Total) : "—", B("#d8e4ee"),
            b.IsHull ? new List<TagVm> { new("HULL", TagBuy, TagBuyB) } : new List<TagVm>())).ToList();
        var dest = plan.Ships.FirstOrDefault()?.StationId ?? 0;
        BuyTotalText = Formatting.Isk(plan.BuyTotal);
        BuyTotalSub = dest != 0 ? $"Haul to {StationName(dest)}" : "";
        ShoppingHeader = $"SHOPPING LIST · {hub.Label} {_priceSide.ToUpperInvariant()}";
        LeftoverRows = plan.Leftovers.Select(l => new ListRowVm(tn(l.TypeId),
            $"Stays in {CharName(l.CharId)}'s hangar · from {ShipLabel(l.FromShipKey)}", "", B("#6b7887"),
            "× " + Formatting.Qty(l.Qty), "", B("#6b7887"), new List<TagVm>())).ToList();

        var last = snap.Characters.Select(c => c.LastSyncAt).Where(t => t != null).DefaultIfEmpty(null).Max();
        RefitFooterText = $"PLAN · {plan.Ships.Count} SHIPS · FITS FROM ESI ASSETS · SYNCED {Formatting.Ago(last).ToUpperInvariant()}";
    }

    private PlanOptions Options() => new()
    {
        UseSwaps = _useSwaps, UseStock = _useStock, AllowContracts = _allowContracts,
        IncludeDrones = _includeDrones, IncludeCharges = _includeCharges, IncludeCargo = _includeCargo,
        PriceSide = _priceSide == "buy" ? "buy" : "sell", Types = _snap.Types, Jumps = StationJumps,
    };

    private static (string, IBrush) SlotStatus(List<SlotCheck> checks, long hull, Func<long, string> tn)
    {
        var main = checks.FirstOrDefault(c => c.HullTypeId == hull) ?? checks.FirstOrDefault();
        if (main == null) return ("", B("#8fa0b0"));
        string Text(SlotCheck c) => c.Status switch
        {
            Services.SlotStatus.Over => $"{c.Used} / {c.Cap} · OVER",
            Services.SlotStatus.Under => $"{c.Used} / {c.Cap} · {c.Cap - c.Used} EMPTY",
            Services.SlotStatus.Ok => $"{c.Used} / {c.Cap}",
            _ => $"{c.Used} · SLOTS UNKNOWN",
        };
        var text = Text(main);
        foreach (var other in checks.Where(c => c != main && c.Status == Services.SlotStatus.Over))
            text += $" · {tn(other.HullTypeId).ToUpperInvariant()} {other.Used} / {other.Cap} OVER";
        var worst = checks.Any(c => c.Status == Services.SlotStatus.Over) ? Services.SlotStatus.Over : main.Status;
        return (text, worst switch
        {
            Services.SlotStatus.Over => B("#c0533f"),
            Services.SlotStatus.Under => B("#ffb300"),
            Services.SlotStatus.Ok => B("#7c9a5e"),
            _ => B("#6b7887"),
        });
    }
}
