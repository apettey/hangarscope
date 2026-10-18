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

    // --fit-sync=<name part> opens the picker on that library fit, --fit-sync-pick=all ticks every pilot (doc screenshot automation)
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
        var fit = _fitSyncFit;
        if (fit == null) return;
        var statuses = _fitSyncStatuses = Classify(fit);
        _fitSyncSel.IntersectWith(statuses.Where(s => s.Selectable).Select(s => s.CharId));
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
        _fitSyncSel.UnionWith(_fitSyncStatuses.Where(s => s.Selectable).Select(s => s.CharId));
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
    }

    [RelayCommand] private void DismissFitSyncResults() => HasFitSyncResults = false;

    [RelayCommand]
    private void ReviewFitSync()
    {
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
        var good = results.Count(r => r.Ok);
        FitSyncResultsTitle = $"“{fit.Name}” · {good} OF {results.Count} PILOTS DONE".ToUpperInvariant();
        FitSyncResults = results.Select(r =>
        {
            var c = chars.GetValueOrDefault(r.CharId);
            return new FitSyncResultVm(Name(r.CharId), c != null ? B(c.Color) : StDim, r.Message.ToUpperInvariant(),
                r.Ok ? StHas : r.Saved ? StOld : StNoScope);
        }).ToList();
        HasFitSyncResults = true;
        RefitStatusText = good == results.Count ? $"FITTING SYNCED TO {good} PILOT{(good == 1 ? "" : "S")}" : $"{results.Count - good} OF {results.Count} PILOTS NEED ATTENTION";
    }

    private static string StripName(string message, string name) =>
        message.StartsWith(name + ": ") ? message[(name.Length + 2)..] : message;

    private void AutoOpenFitSync()
    {
        if (_autoFitSync is not { } q) return;
        _autoFitSync = null;
        var fit = _snap.Fittings.FirstOrDefault(f => f.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        if (fit == null) return;
        OpenFitSync(fit);
        if (Environment.GetCommandLineArgs().Contains("--fit-sync-pick=all")) FitSyncAll();
    }
}
