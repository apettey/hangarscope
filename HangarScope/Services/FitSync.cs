using HangarScope.Models;

namespace HangarScope.Services;

/// <summary>Where one character stands for one fit: has an identical copy, has an outdated same-named copy, has neither, or can't be written to.</summary>
public enum FitCopyState { Has, Outdated, Missing, NoScope }

/// <summary>
/// One character's state for a fit. <see cref="OldCopies"/> are its same-hull, same-named fits whose contents differ:
/// the copies a replacement deletes. A character without the write scope keeps its old copies listed but is <see cref="FitCopyState.NoScope"/>.
/// </summary>
public sealed record FitCopyStatus(long CharId, FitCopyState State, IReadOnlyList<SavedFitting> OldCopies)
{
    public bool Selectable => State is FitCopyState.Outdated or FitCopyState.Missing;
}

public enum FitSyncAction { Nothing, Save, Replace, SaveKeepOld }

/// <summary>What a confirmed sync does on one character.</summary>
public sealed record FitSyncStep(long CharId, FitSyncAction Action, IReadOnlyList<SavedFitting> OldCopies);

/// <summary>Outcome on one character. <see cref="Deleted"/> counts old copies removed after the save.</summary>
public sealed record FitSyncResult(long CharId, FitSyncAction Action, bool Saved, int Deleted, bool Ok, string Message);

/// <summary>All saved fittings indexed by content hash and by (hull, name), for per-character classification.</summary>
public sealed class FitIndex
{
    private readonly Dictionary<string, List<SavedFitting>> _byHash = new();
    private readonly Dictionary<(long hull, string name), List<SavedFitting>> _byName = new();

    public FitIndex(IEnumerable<SavedFitting> fittings)
    {
        foreach (var f in fittings)
        {
            var h = FitMath.Hash(f.ShipTypeId, f.Items);
            (_byHash.TryGetValue(h, out var a) ? a : _byHash[h] = new()).Add(f);
            var k = (f.ShipTypeId, FitSync.NameKey(f.Name));
            (_byName.TryGetValue(k, out var b) ? b : _byName[k] = new()).Add(f);
        }
    }

    public IReadOnlyList<SavedFitting> WithHash(string hash) => _byHash.TryGetValue(hash, out var l) ? l : Array.Empty<SavedFitting>();

    public IReadOnlyList<SavedFitting> Named(long hull, string name) =>
        _byName.TryGetValue((hull, FitSync.NameKey(name)), out var l) ? l : Array.Empty<SavedFitting>();
}

/// <summary>
/// Pushes one fit to chosen characters. Pure apart from the save/delete delegates, so the rules are unit-tested:
/// a copy is outdated when it has the same hull and name (as ESI stores it: trimmed, 50 chars, any case) but different
/// contents; a replacement saves the new copy first and deletes the old ones only after that save succeeded, so a
/// character is never left without the fit.
/// </summary>
public static class FitSync
{
    /// <summary>ESI keeps at most 50 characters of a fitting name.</summary>
    public const int MaxNameLength = 50;

    public static string NameKey(string name)
    {
        var n = name.Trim();
        if (n.Length > MaxNameLength) n = n[..MaxNameLength].TrimEnd();
        return n.ToLowerInvariant();
    }

    public static bool CanWrite(CharacterInfo c) => c.ScopesGranted.Contains(EsiScopes.FittingsWrite);

    /// <summary>One status per character, in the given order.</summary>
    public static List<FitCopyStatus> Classify(IEnumerable<CharacterInfo> characters, FitIndex index, long shipTypeId, string name, IEnumerable<FitItem> items)
    {
        var hash = FitMath.Hash(shipTypeId, items);
        var have = index.WithHash(hash).Select(f => f.CharId).ToHashSet();
        var named = index.Named(shipTypeId, name).Where(f => FitMath.Hash(f.ShipTypeId, f.Items) != hash).ToLookup(f => f.CharId);
        return characters.Select(c =>
        {
            if (have.Contains(c.Id)) return new FitCopyStatus(c.Id, FitCopyState.Has, Array.Empty<SavedFitting>());
            var old = named[c.Id].ToList();
            var state = !CanWrite(c) ? FitCopyState.NoScope : old.Count > 0 ? FitCopyState.Outdated : FitCopyState.Missing;
            return new FitCopyStatus(c.Id, state, old);
        }).ToList();
    }

    /// <summary>Missing characters start selected; outdated ones are opt-in.</summary>
    public static HashSet<long> DefaultSelection(IEnumerable<FitCopyStatus> statuses) =>
        statuses.Where(s => s.State == FitCopyState.Missing).Select(s => s.CharId).ToHashSet();

    /// <summary>One step per character. Unselectable or unselected characters get <see cref="FitSyncAction.Nothing"/>.</summary>
    public static List<FitSyncStep> Plan(IEnumerable<FitCopyStatus> statuses, IReadOnlySet<long> selected, bool replaceOutdated) =>
        statuses.Select(s =>
        {
            if (!s.Selectable || !selected.Contains(s.CharId)) return new FitSyncStep(s.CharId, FitSyncAction.Nothing, Array.Empty<SavedFitting>());
            if (s.State == FitCopyState.Missing) return new FitSyncStep(s.CharId, FitSyncAction.Save, Array.Empty<SavedFitting>());
            return replaceOutdated
                ? new FitSyncStep(s.CharId, FitSyncAction.Replace, s.OldCopies)
                : new FitSyncStep(s.CharId, FitSyncAction.SaveKeepOld, Array.Empty<SavedFitting>());
        }).ToList();

    /// <summary>
    /// Runs the steps one character at a time. Each character is saved first; its old copies are deleted only
    /// when that save succeeded. A failed save or delete never stops the other characters.
    /// </summary>
    public static async Task<List<FitSyncResult>> Execute(IEnumerable<FitSyncStep> steps,
        Func<long, Task<(bool ok, string message)>> save,
        Func<long, long, Task<(bool ok, string message)>> delete)
    {
        var results = new List<FitSyncResult>();
        foreach (var step in steps)
        {
            if (step.Action == FitSyncAction.Nothing) continue;
            var (saved, saveMsg) = await save(step.CharId);
            if (!saved)
            {
                results.Add(new FitSyncResult(step.CharId, step.Action, false, 0, false,
                    "Save failed: " + saveMsg + (step.Action == FitSyncAction.Replace ? " · old copy kept" : "")));
                continue;
            }
            if (step.Action != FitSyncAction.Replace)
            {
                results.Add(new FitSyncResult(step.CharId, step.Action, true, 0, true,
                    step.Action == FitSyncAction.SaveKeepOld ? "Saved · old copy kept" : "Saved"));
                continue;
            }
            var deleted = 0;
            var failures = new List<string>();
            foreach (var old in step.OldCopies)
            {
                var (ok, msg) = await delete(step.CharId, old.FittingId);
                if (ok) deleted++; else failures.Add(msg);
            }
            results.Add(failures.Count == 0
                ? new FitSyncResult(step.CharId, step.Action, true, deleted, true, deleted == 1 ? "Replaced" : $"Replaced {deleted} old copies")
                : new FitSyncResult(step.CharId, step.Action, true, deleted, false,
                    $"Saved, but {failures.Count} old cop{(failures.Count == 1 ? "y" : "ies")} could not be deleted: " + string.Join(" · ", failures)));
        }
        return results;
    }
}
