using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HangarScope.Models;
using HangarScope.Services;

namespace HangarScope.ViewModels;

/// <summary>A row in the fit picker: one merged fit (with its variants collapsed), or one variant when expanded.</summary>
public sealed record FitRowVm(
    bool IsVariant, string Title, string Sub, List<PilotDotVm> Pilots, string PilotsText,
    string Badge, bool CanExpand, string ExpandGlyph, ICommand? ToggleExpand, string DiffText,
    FitGroup Group, FitFamily Family)
{
    public bool HasBadge => Badge.Length > 0;
    public bool HasDiff => DiffText.Length > 0;
    public bool IsMain => !IsVariant;
}

public sealed record PickChipVm(string Label, bool Selected, ICommand Toggle);

public sealed record SlotSummaryVm(string Group, List<string> Lines);

public sealed record PreviewPilotVm(string Name, IBrush Color, bool Has, string Text);

/// <summary>
/// The fit picker shared by REFIT's IN-GAME FIT ▾ flyout and the FITTINGS library: search, hull chips, sort,
/// a virtualized list of merged fits and a preview of the highlighted one. The rules live in <see cref="FitCatalog"/>.
/// </summary>
public sealed partial class FitPickerVm : ObservableObject
{
    private List<FitFamily> _families = new();
    private List<CharacterInfo> _chars = new();
    private Func<long, string> _typeName = id => $"Type {id}";
    private FitIndex _index = new(Array.Empty<SavedFitting>());
    private readonly HashSet<long> _hulls = new();
    private bool _hullsTouched;
    // --fit-expand=all opens every variant group (doc screenshot automation)
    private bool _expandAll = Environment.GetCommandLineArgs().Contains("--fit-expand=all");
    private readonly HashSet<string> _expanded = new();
    private string? _selectedHash;
    private string _query = "";
    private FitSort _sort = FitSort.Name;
    private FitRowVm? _selectedRow;

    public required Action<FitGroup> OnUse { get; init; }
    public required Action<FitGroup> OnSyncTo { get; init; }
    public required Action<FitGroup> OnTidy { get; init; }
    public required Action<FitGroup> OnDelete { get; init; }

    [ObservableProperty] private List<FitRowVm> _rows = new();
    [ObservableProperty] private List<PickChipVm> _hullChips = new();
    [ObservableProperty] private List<PickChipVm> _sortChips = new();
    [ObservableProperty] private string _headerText = "";
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _hasPreview;
    [ObservableProperty] private string _previewTitle = "";
    [ObservableProperty] private string _previewSub = "";
    [ObservableProperty] private string _previewNames = "";
    [ObservableProperty] private string _previewVariant = "";
    [ObservableProperty] private List<SlotSummaryVm> _previewSlots = new();
    [ObservableProperty] private List<PreviewPilotVm> _previewPilots = new();
    [ObservableProperty] private string _tidyText = "";
    [ObservableProperty] private bool _canTidy;

    public bool HasPreviewNames => PreviewNames.Length > 0;
    public bool HasPreviewVariant => PreviewVariant.Length > 0;
    public bool HasTidyText => TidyText.Length > 0;
    partial void OnPreviewNamesChanged(string value) => OnPropertyChanged(nameof(HasPreviewNames));
    partial void OnPreviewVariantChanged(string value) => OnPropertyChanged(nameof(HasPreviewVariant));
    partial void OnTidyTextChanged(string value) => OnPropertyChanged(nameof(HasTidyText));

    public string Query
    {
        get => _query;
        set { if (SetProperty(ref _query, value)) Refresh(); }
    }

    public FitRowVm? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (!SetProperty(ref _selectedRow, value)) return;
            if (value != null) _selectedHash = value.Group.Hash;
            UpdatePreview();
        }
    }

    public ICommand UseCommand => new RelayCommand(() => { if (_selectedRow != null) OnUse(_selectedRow.Group); });
    public ICommand SyncToCommand => new RelayCommand(() => { if (_selectedRow != null) OnSyncTo(_selectedRow.Group); });
    public ICommand TidyCommand => new RelayCommand(() => { if (_selectedRow != null) OnTidy(_selectedRow.Group); });
    public ICommand DeleteCommand => new RelayCommand(() => { if (_selectedRow != null) OnDelete(_selectedRow.Group); });

    /// <summary>New snapshot. <paramref name="defaultHulls"/> (the selected ships' hulls) pre-filter until the user picks hull chips.</summary>
    public void Load(List<FitFamily> families, List<CharacterInfo> chars, Func<long, string> typeName, IReadOnlySet<long> defaultHulls, FitIndex index)
    {
        _families = families;
        _index = index;
        if (_expandAll) { _expandAll = false; _expanded.UnionWith(families.Where(f => f.HasVariants).Select(f => f.Main.Hash)); }
        _chars = chars;
        _typeName = typeName;
        if (!_hullsTouched)
        {
            _hulls.Clear();
            _hulls.UnionWith(defaultHulls.Where(h => families.Any(f => f.ShipTypeId == h)));
        }
        Refresh();
    }

    /// <summary>Arrow keys in the search box: move the highlight.</summary>
    public void Move(int delta)
    {
        if (Rows.Count == 0) return;
        var i = _selectedRow == null ? -1 : Rows.IndexOf(_selectedRow);
        SelectedRow = Rows[Math.Clamp(i + delta, 0, Rows.Count - 1)];
    }

    /// <summary>Enter or double-click: use the highlighted fit as the target.</summary>
    public bool Activate()
    {
        if (_selectedRow == null) return false;
        OnUse(_selectedRow.Group);
        return true;
    }

    private void Refresh()
    {
        var tn = _typeName;
        var searched = FitCatalog.Search(_families, _query, new HashSet<long>(), _sort, tn);
        var byHull = searched.GroupBy(f => f.ShipTypeId).ToDictionary(g => g.Key, g => g.Count());
        _hulls.IntersectWith(_families.Select(f => f.ShipTypeId));
        var shown = _hulls.Count == 0 ? searched : searched.Where(f => _hulls.Contains(f.ShipTypeId)).ToList();

        var chips = new List<PickChipVm> { new($"ALL · {searched.Count}", _hulls.Count == 0, new RelayCommand(() => { _hullsTouched = true; _hulls.Clear(); Refresh(); })) };
        foreach (var (hull, n) in byHull.OrderByDescending(kv => _hulls.Contains(kv.Key)).ThenByDescending(kv => kv.Value).ThenBy(kv => tn(kv.Key))
                     .Take(Math.Max(12, _hulls.Count)))
            chips.Add(new PickChipVm($"{tn(hull).ToUpperInvariant()} · {n}", _hulls.Contains(hull), new RelayCommand(() =>
            {
                _hullsTouched = true;
                if (!_hulls.Remove(hull)) _hulls.Add(hull);
                Refresh();
            })));
        HullChips = chips;
        SortChips = new List<PickChipVm>
        {
            new("NAME", _sort == FitSort.Name, new RelayCommand(() => { _sort = FitSort.Name; Refresh(); })),
            new("MOST PILOTS", _sort == FitSort.Pilots, new RelayCommand(() => { _sort = FitSort.Pilots; Refresh(); })),
            new("NEWEST", _sort == FitSort.Newest, new RelayCommand(() => { _sort = FitSort.Newest; Refresh(); })),
        };

        var rows = new List<FitRowVm>();
        foreach (var f in shown)
        {
            var key = f.Main.Hash;
            var open = _expanded.Contains(key);
            var toggle = f.HasVariants ? new RelayCommand(() => { if (!_expanded.Remove(key)) _expanded.Add(key); Refresh(); }) : null;
            rows.Add(Row(f, f.Main, false, FitCatalog.VariantLabel(f).ToUpperInvariant(), open ? "▾" : "▸", toggle, ""));
            if (!open) continue;
            foreach (var g in f.Groups)
                rows.Add(Row(f, g, true, g == f.Main ? "MAIN" : "", "", null, g == f.Main ? "Main fit: most pilots" : DiffText(f.Main, g)));
        }
        Rows = rows;
        IsEmpty = rows.Count == 0;
        var copies = _families.Sum(f => f.Groups.Sum(g => g.Copies.Count));
        HeaderText = $"{shown.Count} OF {_families.Count} FITS · {_families.Sum(f => f.Groups.Count)} DISTINCT · {copies} COPIES ACROSS {_chars.Count} PILOTS";
        SelectedRow = rows.FirstOrDefault(r => r.Group.Hash == _selectedHash && r.IsMain) ?? rows.FirstOrDefault(r => r.Group.Hash == _selectedHash) ?? rows.FirstOrDefault();
        if (SelectedRow != null && ReferenceEquals(SelectedRow, _selectedRow)) UpdatePreview();
    }

    private FitRowVm Row(FitFamily f, FitGroup g, bool variant, string badge, string glyph, ICommand? toggle, string diff)
    {
        var have = g.CharIds;
        var text = $"{have.Count} OF {_chars.Count} PILOTS";
        if (!variant)
        {
            // missing vs outdated: same rules as SYNC TO… (FitSync)
            var lacking = FitSync.Classify(_chars, _index, g.ShipTypeId, g.Name, g.Items).Where(s => s.State != FitCopyState.Has).ToList();
            var outdated = lacking.Count(s => s.OldCopies.Count > 0);
            if (lacking.Count - outdated > 0) text += $" · {lacking.Count - outdated} MISSING";
            if (outdated > 0) text += $" · {outdated} OUTDATED";
        }
        var extra = g.Names.Count > 1 ? $" · also “{string.Join("”, “", g.Names.Skip(1))}”" : "";
        return new FitRowVm(variant, g.Name, _typeName(g.ShipTypeId) + extra,
            _chars.Select(c => new PilotDotVm(c.Name, MainViewModel.B(c.Color), have.Contains(c.Id))).ToList(),
            text, badge, toggle != null, glyph, toggle, diff, g, f);
    }

    private string DiffText(FitGroup main, FitGroup g)
    {
        var d = FitCatalog.Deltas(main.Items, g.Items);
        var parts = d.Take(4).Select(x => $"{(x.Delta > 0 ? "+" : "−")}{Math.Abs(x.Delta)} {_typeName(x.TypeId)} ({x.Group.ToLowerInvariant()})");
        return string.Join(" · ", parts) + (d.Count > 4 ? $" · +{d.Count - 4} more" : "");
    }

    private void UpdatePreview()
    {
        var r = _selectedRow;
        HasPreview = r != null;
        if (r == null) { PreviewSlots = new(); PreviewPilots = new(); CanTidy = false; TidyText = ""; return; }
        var g = r.Group;
        var tn = _typeName;
        PreviewTitle = g.Name;
        PreviewSub = $"{tn(g.ShipTypeId)} · {g.Items.Count} lines · {g.Copies.Count} cop{(g.Copies.Count == 1 ? "y" : "ies")}";
        PreviewNames = g.Names.Count > 1 ? "SAVED AS " + string.Join(" · ", g.Names.Select(n => $"“{n}”")) : "";
        PreviewVariant = !r.Family.HasVariants ? ""
            : g == r.Family.Main ? $"MAIN FIT · {FitCatalog.VariantLabel(r.Family).ToUpperInvariant()}"
            : $"VARIANT OF “{r.Family.Main.Name}” · {DiffText(r.Family.Main, g)}";
        PreviewSlots = FitCatalog.SlotOrder
            .Select(s => (s, items: g.Items.Where(i => FitMath.SlotGroup(i.Flag) == s).GroupBy(i => i.TypeId)
                .Select(x => (t: x.Key, q: x.Sum(i => i.Qty))).OrderByDescending(x => x.q).ThenBy(x => tn(x.t)).ToList()))
            .Where(x => x.items.Count > 0 || x.s is "HIGH" or "MED" or "LOW" or "RIG")
            .Select(x => new SlotSummaryVm(x.s, x.items.Count == 0 ? new List<string> { "—" } : x.items.Select(i => $"{i.q} × {tn(i.t)}").ToList()))
            .ToList();
        var byChar = g.Copies.ToLookup(c => c.CharId);
        var familyChars = r.Family.Groups.Where(x => x != g).SelectMany(x => x.Copies).Select(c => c.CharId).ToHashSet();
        PreviewPilots = _chars.Select(c =>
        {
            var mine = byChar[c.Id].ToList();
            var text = mine.Count switch
            {
                0 => familyChars.Contains(c.Id) ? "has a variant" : "not saved",
                1 => $"“{mine[0].Name}”",
                _ => $"{mine.Count} copies · " + string.Join(", ", mine.Select(m => $"“{m.Name}”")),
            };
            return new PreviewPilotVm(c.Name, MainViewModel.B(c.Color), mine.Count > 0, text);
        }).ToList();
        var dupes = byChar.Count(x => x.Count() > 1);
        CanTidy = dupes > 0;
        TidyText = dupes > 0 ? $"{dupes} PILOT{(dupes == 1 ? "" : "S")} HOLD{(dupes == 1 ? "S" : "")} DUPLICATE COPIES" : "";
    }
}
