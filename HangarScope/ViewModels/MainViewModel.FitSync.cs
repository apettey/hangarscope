using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HangarScope.Models;
using HangarScope.Services;

namespace HangarScope.ViewModels;

// REFIT · SYNC TO picker: choose which characters get a fit, replace outdated same-named copies, report per character.
public sealed partial class MainViewModel
{
    private SavedFitting? _fitSyncFit;
    private List<FitCopyStatus> _fitSyncStatuses = new();
    private readonly HashSet<long> _fitSyncSel = new();
    private bool _replaceOutdated = true;
    private readonly HashSet<long> _fitSyncPickable = new();
    // TIDY DUPLICATES mode: same picker, deletes a pilot's extra identical copies
    private FitGroup? _tidyGroup;
    private string _tidyKeepName = "";
    private int _tidyKeepIndex;

    // --fit-sync=<name part> opens the picker on that library fit, --fit-sync-pick=all ticks every pilot (doc screenshot automation)
    private string? _autoFitTidy = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--fit-tidy="))?["--fit-tidy=".Length..];
    private string? _autoFitSync = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--fit-sync="))?["--fit-sync=".Length..];

    [ObservableProperty] private bool _isFitSyncOpen;
    [ObservableProperty] private string _fitSyncTitle = "";
    [ObservableProperty] private string _fitSyncSub = "";
    [ObservableProperty] private List<FitSyncPilotVm> _fitSyncPilots = new();
    [ObservableProperty] private string _fitSyncPlanText = "";
    [ObservableProperty] private bool _fitSyncHasOutdated;
    [ObservableProperty] private bool _hasFitSyncResults;
    [ObservableProperty] private string _fitSyncResultsTitle = "";
    [ObservableProperty] private List<FitSyncResultVm> _fitSyncResults = new();
    [ObservableProperty] private bool _isTidyMode;
    [ObservableProperty] private List<string> _tidyNameOptions = new();

    /// <summary>Which of the fit's names each tidied pilot keeps.</summary>
    public int TidyKeepIndex
    {
        get => _tidyKeepIndex;
        set
        {
            if (!SetProperty(ref _tidyKeepIndex, value) || value < 0 || value >= TidyNameOptions.Count) return;
            _tidyKeepName = TidyNameOptions[value];
            RebuildFitSync();
        }
    }

    public bool ReplaceOutdated
    {
        get => _replaceOutdated;
        set { if (SetProperty(ref _replaceOutdated, value)) RebuildFitSync(); }
    }

    private static readonly IBrush StHas = B("#7c9a5e"), StOld = B("#ffb300"), StMissing = B("#4fc3f7"), StNoScope = B("#c0533f"), StDim = B("#6b7887");

    private void OpenFitSync(SavedFitting fit)
    {
        _pendingWrite = null;
        HasPendingWrite = false;
        HasFitSyncResults = false;
        _tidyGroup = null;
        IsTidyMode = false;
        _fitSyncFit = fit;
        _fitSyncSel.Clear();
        _fitSyncSel.UnionWith(FitSync.DefaultSelection(Classify(fit)));
        IsFitSyncOpen = true;
        RebuildFitSync();
    }

    private List<FitCopyStatus> Classify(SavedFitting fit) =>
        FitSync.Classify(_snap.Characters, new FitIndex(_snap.Fittings), fit.ShipTypeId, fit.Name, fit.Items);

    /// <summary>Re-reads every character's state from the current snapshot; called on open, on each tick and after syncs.</summary>
    private void RebuildFitSync()
    {
        if (_tidyGroup != null) { RebuildTidy(); return; }
        var fit = _fitSyncFit;
        if (fit == null) return;
        var statuses = _fitSyncStatuses = Classify(fit);
        _fitSyncPickable.Clear();
        _fitSyncPickable.UnionWith(statuses.Where(s => s.Selectable).Select(s => s.CharId));
        _fitSyncSel.IntersectWith(_fitSyncPickable);
        var chars = _snap.Characters.ToDictionary(c => c.Id);

        FitSyncTitle = $"SYNC “{fit.Name}” TO…".ToUpperInvariant();
        FitSyncSub = $"{_typeName(fit.ShipTypeId)} · {fit.Items.Count} lines · matched by contents; a same-named {_typeName(fit.ShipTypeId)} fit with other contents is outdated";
        FitSyncHasOutdated = statuses.Any(s => s.State == FitCopyState.Outdated);
        FitSyncPilots = statuses.Select(s =>
        {
            var c = chars[s.CharId];
            var (text, brush) = s.State switch
            {
                FitCopyState.Has => ("HAS IT", StHas),
                FitCopyState.Outdated => (s.OldCopies.Count == 1 ? "OUTDATED COPY" : $"{s.OldCopies.Count} OUTDATED COPIES", StOld),
                FitCopyState.Missing => ("MISSING", StMissing),
                _ => ("NO FITTINGS SCOPE", StNoScope),
            };
            var hint = s.State == FitCopyState.NoScope ? "Re-link in CHARACTERS to grant " + EsiScopes.FittingsWrite : "";
            var id = s.CharId;
            return new FitSyncPilotVm(c.Name, B(c.Color), text, brush, hint, s.Selectable, _fitSyncSel.Contains(id),
                new RelayCommand(() =>
                {
                    if (!_fitSyncSel.Remove(id)) _fitSyncSel.Add(id);
                    RebuildFitSync();
                }));
        }).ToList();
        FitSyncPlanText = DescribeSteps(FitSync.Plan(statuses, _fitSyncSel, _replaceOutdated), chars);
    }

    private static string DescribeSteps(List<FitSyncStep> steps, Dictionary<long, CharacterInfo> chars)
    {
        string Names(FitSyncAction a) => string.Join(", ", steps.Where(s => s.Action == a).Select(s => chars.TryGetValue(s.CharId, out var c) ? c.Name : $"Character {s.CharId}"));
        var parts = new List<string>();
        void Part(FitSyncAction a, string label) { var n = Names(a); if (n.Length > 0) parts.Add(label + n); }
        Part(FitSyncAction.Save, "SAVE ON ");
        Part(FitSyncAction.Replace, "REPLACE ON ");
        Part(FitSyncAction.SaveKeepOld, "SAVE, KEEP OLD COPY ON ");
        Part(FitSyncAction.Nothing, "NOTHING ON ");
        return string.Join(" · ", parts).ToUpperInvariant();
    }

    [RelayCommand]
    private void FitSyncAll()
    {
        _fitSyncSel.UnionWith(_fitSyncPickable);
        RebuildFitSync();
    }

    [RelayCommand]
    private void FitSyncNone()
    {
        _fitSyncSel.Clear();
        RebuildFitSync();
    }

    [RelayCommand]
    private void CancelFitSync()
    {
        IsFitSyncOpen = false;
        _fitSyncFit = null;
        _tidyGroup = null;
    }

    [RelayCommand] private void DismissFitSyncResults() => HasFitSyncResults = false;

    [RelayCommand]
    private void ReviewFitSync()
    {
        if (_tidyGroup != null) { ReviewTidy(); return; }
        var fit = _fitSyncFit;
        if (fit == null) return;
        var steps = FitSync.Plan(_fitSyncStatuses, _fitSyncSel, _replaceOutdated);
        if (steps.All(s => s.Action == FitSyncAction.Nothing)) { RefitStatusText = "TICK AT LEAST ONE PILOT"; return; }
        var chars = _snap.Characters.ToDictionary(c => c.Id);
        var replacing = steps.Any(s => s.Action == FitSyncAction.Replace);
        AskWrite($"SYNC FITTING “{fit.Name}” ({_typeName(fit.ShipTypeId)}, {fit.Items.Count} LINES) · ".ToUpperInvariant() + DescribeSteps(steps, chars) +
                 (replacing ? " · REPLACE SAVES THE NEW COPY FIRST AND DELETES THE OLD ONE ONLY IF THAT SAVE WORKED" : ""),
            () => RunFitSync(fit, steps));
    }

    private async Task RunFitSync(SavedFitting fit, List<FitSyncStep> steps)
    {
        _fitSyncFit = null;
        var chars = _snap.Characters.ToDictionary(c => c.Id);
        string Name(long id) => chars.TryGetValue(id, out var c) ? c.Name : $"Character {id}";
        RefitStatusText = "WRITING FITTING…";
        // SaveFitting/DeleteFitting stamp each character's write time, so a stale ESI list can't undo this (SyncService.IsStaleRead)
        var results = await FitSync.Execute(steps,
            async id =>
            {
                var r = await _sync.SaveFitting(new[] { id }, fit);
                return r.Count == 0 ? (false, "no response") : (r[0].ok, StripName(r[0].message, Name(id)));
            },
            async (id, fittingId) =>
            {
                var (ok, msg) = await _sync.DeleteFitting(id, fittingId);
                return (ok, StripName(msg, Name(id)));
            });
        ShowResults($"SYNC “{fit.Name}”", results, chars);
    }

    // ------------------------------------------------------------------ tidy duplicates

    private void OpenTidy(FitGroup group)
    {
        _pendingWrite = null;
        HasPendingWrite = false;
        HasFitSyncResults = false;
        _fitSyncFit = null;
        _tidyGroup = group;
        IsTidyMode = true;
        _tidyKeepName = group.Name;
        TidyNameOptions = group.Names.ToList();
        _tidyKeepIndex = 0;
        OnPropertyChanged(nameof(TidyKeepIndex));
        var chars = _snap.Characters.ToDictionary(c => c.Id);
        _fitSyncSel.Clear();
        _fitSyncSel.UnionWith(group.Copies.GroupBy(c => c.CharId)
            .Where(g => g.Count() > 1 && chars.TryGetValue(g.Key, out var c) && FitSync.CanWrite(c)).Select(g => g.Key));
        IsFitSyncOpen = true;
        RebuildFitSync();
    }

    /// <summary>The group as it stands in the current snapshot (copies disappear as they are deleted).</summary>
    private FitGroup CurrentTidyGroup(FitGroup g) =>
        FitCatalog.Group(_snap.Fittings.Where(f => f.ShipTypeId == g.ShipTypeId)).FirstOrDefault(x => x.Hash == g.Hash)
        ?? new FitGroup { Hash = g.Hash, ShipTypeId = g.ShipTypeId, SlotHash = g.SlotHash, Copies = new(), Names = g.Names };

    private void RebuildTidy()
    {
        var g = CurrentTidyGroup(_tidyGroup!);
        var chars = _snap.Characters.ToDictionary(c => c.Id);
        var byChar = g.Copies.ToLookup(c => c.CharId);
        _fitSyncPickable.Clear();
        _fitSyncPickable.UnionWith(_snap.Characters.Where(c => byChar[c.Id].Count() > 1 && FitSync.CanWrite(c)).Select(c => c.Id));
        _fitSyncSel.IntersectWith(_fitSyncPickable);
        FitSyncTitle = $"TIDY DUPLICATES OF “{_tidyGroup!.Name}”".ToUpperInvariant();
        FitSyncSub = $"{_typeName(g.ShipTypeId)} · only identical copies are deleted; variants with other cargo, drones or modules are never touched";
        FitSyncHasOutdated = false;
        FitSyncPilots = _snap.Characters.Select(c =>
        {
            var n = byChar[c.Id].Count();
            var (text, brush) = n > 1 && !FitSync.CanWrite(c) ? ("NO FITTINGS SCOPE", StNoScope)
                : n > 1 ? ($"{n} COPIES · DELETE {n - 1}", StOld)
                : n == 1 ? ("1 COPY · NOTHING TO TIDY", StHas)
                : ("NOT SAVED", StDim);
            var hint = n > 1 && !FitSync.CanWrite(c) ? "Re-link in CHARACTERS to grant " + EsiScopes.FittingsWrite : "";
            var id = c.Id;
            return new FitSyncPilotVm(c.Name, B(c.Color), text, brush, hint, _fitSyncPickable.Contains(id), _fitSyncSel.Contains(id),
                new RelayCommand(() =>
                {
                    if (!_fitSyncSel.Remove(id)) _fitSyncSel.Add(id);
                    RebuildFitSync();
                }));
        }).ToList();
        FitSyncPlanText = DescribeTidy(TidySteps(g), chars);
    }

    private List<TidyStep> TidySteps(FitGroup g) => FitCatalog.PlanTidy(g, _tidyKeepName).Where(s => _fitSyncSel.Contains(s.CharId)).ToList();

    private string DescribeTidy(List<TidyStep> steps, Dictionary<long, CharacterInfo> chars)
    {
        string Name(long id) => chars.TryGetValue(id, out var c) ? c.Name : $"Character {id}";
        var parts = steps.Select(s => $"{Name(s.CharId)}: keep “{s.Keep.Name}” #{s.Keep.FittingId}, delete " +
                                      string.Join(", ", s.Delete.Select(d => $"“{d.Name}” #{d.FittingId}"))).ToList();
        var none = _snap.Characters.Where(c => steps.All(s => s.CharId != c.Id)).Select(c => c.Name).ToList();
        if (none.Count > 0) parts.Add("nothing on " + string.Join(", ", none));
        return string.Join(" · ", parts).ToUpperInvariant();
    }

    private void ReviewTidy()
    {
        var g = CurrentTidyGroup(_tidyGroup!);
        var steps = TidySteps(g);
        if (steps.Count == 0) { RefitStatusText = "TICK AT LEAST ONE PILOT WITH DUPLICATES"; return; }
        var chars = _snap.Characters.ToDictionary(c => c.Id);
        var n = steps.Sum(s => s.Delete.Count);
        AskWrite($"DELETE {n} DUPLICATE COP{(n == 1 ? "Y" : "IES")} OF “{g.Name}” ({_typeName(g.ShipTypeId)}) · ".ToUpperInvariant() + DescribeTidy(steps, chars) +
                 " · THIS CANNOT BE UNDONE",
            () => RunTidy(g, steps));
    }

    private async Task RunTidy(FitGroup g, List<TidyStep> steps)
    {
        _tidyGroup = null;
        var chars = _snap.Characters.ToDictionary(c => c.Id);
        string Name(long id) => chars.TryGetValue(id, out var c) ? c.Name : $"Character {id}";
        RefitStatusText = "DELETING DUPLICATES…";
        var results = await FitCatalog.ExecuteTidy(steps, async (id, fittingId) =>
        {
            var (ok, msg) = await _sync.DeleteFitting(id, fittingId);
            return (ok, StripName(msg, Name(id)));
        });
        ShowResults($"TIDY “{g.Name}”", results, chars);
    }

    private void ShowResults(string title, List<FitSyncResult> results, Dictionary<long, CharacterInfo> chars)
    {
        var good = results.Count(r => r.Ok);
        FitSyncResultsTitle = $"{title} · {good} OF {results.Count} PILOTS DONE".ToUpperInvariant();
        FitSyncResults = results.Select(r =>
        {
            var c = chars.GetValueOrDefault(r.CharId);
            return new FitSyncResultVm(c?.Name ?? $"Character {r.CharId}", c != null ? B(c.Color) : StDim, r.Message.ToUpperInvariant(),
                r.Ok ? StHas : r.Saved || r.Deleted > 0 ? StOld : StNoScope);
        }).ToList();
        HasFitSyncResults = true;
        RefitStatusText = good == results.Count ? $"DONE ON {good} PILOT{(good == 1 ? "" : "S")}" : $"{results.Count - good} OF {results.Count} PILOTS NEED ATTENTION";
    }

    private static string StripName(string message, string name) =>
        message.StartsWith(name + ": ") ? message[(name.Length + 2)..] : message;

    private void AutoOpenFitSync()
    {
        if (_autoFitTidy is { } t)
        {
            _autoFitTidy = null;
            var g = FitCatalog.Group(_snap.Fittings).Where(x => x.Copies.GroupBy(c => c.CharId).Any(c => c.Count() > 1))
                .FirstOrDefault(x => x.Names.Any(n => n.Contains(t, StringComparison.OrdinalIgnoreCase)));
            if (g != null) OpenTidy(g);
        }
        if (_autoFitSync is not { } q) return;
        _autoFitSync = null;
        var fit = _snap.Fittings.FirstOrDefault(f => f.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        if (fit == null) return;
        OpenFitSync(fit);
        if (Environment.GetCommandLineArgs().Contains("--fit-sync-pick=all")) FitSyncAll();
    }
}
