using HangarScope.Models;

namespace HangarScope.Services;

/// <summary>Identical saved fits (same hull and items, any name) across every pilot: one entry in the fit picker.</summary>
public sealed class FitGroup
{
    public required string Hash { get; init; }
    public required long ShipTypeId { get; init; }
    /// <summary>Hash of the module slots only (HIGH/MED/LOW/RIG/SUBSYSTEM): equal for fits that differ only in drones or cargo.</summary>
    public required string SlotHash { get; init; }
    public required List<SavedFitting> Copies { get; init; }
    /// <summary>Every name the fit is saved under, most used first.</summary>
    public required List<string> Names { get; init; }
    public List<FitItem> Items => Copies[0].Items;
    public string Name => Names[0];
    public HashSet<long> CharIds => Copies.Select(c => c.CharId).ToHashSet();
    public int PilotCount => Copies.Select(c => c.CharId).Distinct().Count();
    public long NewestId => Copies.Max(c => c.FittingId);
}

/// <summary>What separates a variant from the family's main fit.</summary>
[Flags]
public enum FitDiff { None = 0, Modules = 1, Drones = 2, Cargo = 4 }

/// <summary>
/// Fits that belong together: identical groups linked when their module slots match (cargo or drones differ)
/// or when they share a name on the same hull (contents differ). <see cref="Groups"/>[0] is the main fit.
/// </summary>
public sealed class FitFamily
{
    public required List<FitGroup> Groups { get; init; }
    public FitGroup Main => Groups[0];
    public long ShipTypeId => Main.ShipTypeId;
    public bool HasVariants => Groups.Count > 1;
    /// <summary>Union of how the variants differ from the main fit.</summary>
    public required FitDiff Diff { get; init; }
    public int PilotCount => Groups.SelectMany(g => g.Copies).Select(c => c.CharId).Distinct().Count();
    public long NewestId => Groups.Max(g => g.NewestId);
    public IEnumerable<string> AllNames => Groups.SelectMany(g => g.Names).Distinct();
}

public enum FitSort { Name, Pilots, Newest }

/// <summary>One character's part in "tidy duplicates": the copy it keeps and the identical copies deleted.</summary>
public sealed record TidyStep(long CharId, SavedFitting Keep, IReadOnlyList<SavedFitting> Delete);

/// <summary>
/// Pure rules behind the REFIT fit picker: merging identical fits, grouping variants, search ranking and the
/// "tidy duplicates" plan. Covered by HangarScope.Tests/FitCatalogTests.
/// </summary>
public static class FitCatalog
{
    private static bool IsModuleGroup(string g) => g is "HIGH" or "MED" or "LOW" or "RIG" or "SUBSYSTEM";

    public static string SlotHash(long shipTypeId, IEnumerable<FitItem> items) =>
        FitMath.Hash(shipTypeId, items.Where(i => IsModuleGroup(FitMath.SlotGroup(i.Flag))));

    /// <summary>One group per distinct hull + items; names ordered by how many copies use them.</summary>
    public static List<FitGroup> Group(IEnumerable<SavedFitting> fittings) =>
        fittings.GroupBy(f => FitMath.Hash(f.ShipTypeId, f.Items)).Select(g =>
        {
            var copies = g.OrderByDescending(f => f.FittingId).ToList();
            return new FitGroup
            {
                Hash = g.Key, ShipTypeId = copies[0].ShipTypeId, SlotHash = SlotHash(copies[0].ShipTypeId, copies[0].Items), Copies = copies,
                Names = copies.GroupBy(c => c.Name.Trim()).OrderByDescending(n => n.Count()).ThenBy(n => n.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(n => n.Key).ToList(),
            };
        }).ToList();

    /// <summary>Which kinds of contents differ between two fits.</summary>
    public static FitDiff Diff(IEnumerable<FitItem> a, IEnumerable<FitItem> b)
    {
        static Dictionary<(string g, long t), long> Count(IEnumerable<FitItem> items) => items
            .GroupBy(i => (FitMath.SlotGroup(i.Flag), i.TypeId)).ToDictionary(x => x.Key, x => x.Sum(i => i.Qty));
        var ca = Count(a);
        var cb = Count(b);
        var diff = FitDiff.None;
        foreach (var k in ca.Keys.Union(cb.Keys))
        {
            if (ca.GetValueOrDefault(k) == cb.GetValueOrDefault(k)) continue;
            diff |= IsModuleGroup(k.g) ? FitDiff.Modules : k.g is "DRONES" or "FIGHTERS" ? FitDiff.Drones : FitDiff.Cargo;
        }
        return diff;
    }

    /// <summary>Per slot group and type, how many more (+) or fewer (−) <paramref name="other"/> has than <paramref name="main"/>.</summary>
    public static List<(string Group, long TypeId, long Delta)> Deltas(IEnumerable<FitItem> main, IEnumerable<FitItem> other)
    {
        static Dictionary<(string g, long t), long> Count(IEnumerable<FitItem> items) => items
            .GroupBy(i => (FitMath.SlotGroup(i.Flag), i.TypeId)).ToDictionary(x => x.Key, x => x.Sum(i => i.Qty));
        var a = Count(main);
        var b = Count(other);
        return a.Keys.Union(b.Keys).Select(k => (k.g, k.t, b.GetValueOrDefault(k) - a.GetValueOrDefault(k)))
            .Where(x => x.Item3 != 0).OrderBy(x => Array.IndexOf(SlotOrder, x.g)).ThenBy(x => x.t).ToList();
    }

    /// <summary>Slot groups in display order.</summary>
    public static readonly string[] SlotOrder = { "HIGH", "MED", "LOW", "RIG", "SUBSYSTEM", "DRONES", "FIGHTERS", "CARGO" };

    /// <summary>
    /// Links identical groups into families: same module slots, or a shared name (trimmed, any case, ESI's 50-char cut)
    /// on the same hull. The main fit is the one on the most pilots, then the newest.
    /// </summary>
    public static List<FitFamily> Families(IEnumerable<FitGroup> groups)
    {
        var list = groups.ToList();
        var parent = Enumerable.Range(0, list.Count).ToArray();
        int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[b] = a; }
        var firstBy = new Dictionary<string, int>();
        void Link(string key, int i) { if (firstBy.TryGetValue(key, out var j)) Union(j, i); else firstBy[key] = i; }
        for (var i = 0; i < list.Count; i++)
        {
            Link("s|" + list[i].SlotHash, i);
            foreach (var n in list[i].Names) Link($"n|{list[i].ShipTypeId}|{FitSync.NameKey(n)}", i);
        }
        return Enumerable.Range(0, list.Count).GroupBy(Find).Select(g =>
        {
            var members = g.Select(i => list[i]).OrderByDescending(x => x.PilotCount).ThenByDescending(x => x.NewestId).ToList();
            var diff = members.Skip(1).Aggregate(FitDiff.None, (d, x) => d | Diff(members[0].Items, x.Items));
            return new FitFamily { Groups = members, Diff = diff };
        }).ToList();
    }

    /// <summary>"3 VARIANTS · CARGO DIFFERS", or "" for a family of one.</summary>
    public static string VariantLabel(FitFamily f)
    {
        if (!f.HasVariants) return "";
        var parts = new List<string>();
        if (f.Diff.HasFlag(FitDiff.Modules)) parts.Add("modules");
        if (f.Diff.HasFlag(FitDiff.Drones)) parts.Add("drones");
        if (f.Diff.HasFlag(FitDiff.Cargo)) parts.Add("cargo");
        return $"{f.Groups.Count} variants · {string.Join(", ", parts)} differ{(parts.Count == 1 ? "s" : "")}";
    }

    /// <summary>
    /// Search score of a family, or null when some query word matches nothing. Each word scores 4 for a name that
    /// starts with it, 3 for a name containing it, 2 for the hull and 1 for a module or item, so name matches rank first.
    /// </summary>
    public static int? Score(FitFamily f, string query, Func<long, string> typeName)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return 0;
        var names = f.AllNames.Select(n => n.ToLowerInvariant()).ToList();
        var hull = typeName(f.ShipTypeId).ToLowerInvariant();
        List<string>? items = null;
        var score = 0;
        foreach (var raw in words)
        {
            var w = raw.ToLowerInvariant();
            if (names.Any(n => n.StartsWith(w) || n.Contains(" " + w))) score += 4;
            else if (names.Any(n => n.Contains(w))) score += 3;
            else if (hull.Contains(w)) score += 2;
            else if ((items ??= f.Groups.SelectMany(g => g.Items).Select(i => i.TypeId).Distinct().Select(t => typeName(t).ToLowerInvariant()).ToList())
                     .Any(n => n.Contains(w))) score += 1;
            else return null;
        }
        return score;
    }

    /// <summary>Filters by query and hulls (empty = all), ranks by score, then by the chosen sort.</summary>
    public static List<FitFamily> Search(IEnumerable<FitFamily> families, string query, IReadOnlySet<long> hulls, FitSort sort, Func<long, string> typeName)
    {
        var scored = families.Where(f => hulls.Count == 0 || hulls.Contains(f.ShipTypeId))
            .Select(f => (f, s: Score(f, query, typeName))).Where(x => x.s != null).ToList();
        var ordered = scored.OrderByDescending(x => x.s);
        ordered = sort switch
        {
            FitSort.Pilots => ordered.ThenByDescending(x => x.f.PilotCount),
            FitSort.Newest => ordered.ThenByDescending(x => x.f.NewestId),
            _ => ordered,
        };
        return ordered.ThenBy(x => x.f.Main.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.f.Main.Hash, StringComparer.Ordinal)
            .Select(x => x.f).ToList();
    }

    /// <summary>
    /// For every character holding two or more identical copies: keep the newest copy named <paramref name="keepName"/>
    /// (or its newest copy when none has that name) and delete the others. Variants are never included.
    /// </summary>
    public static List<TidyStep> PlanTidy(FitGroup group, string keepName)
    {
        var key = FitSync.NameKey(keepName);
        return group.Copies.GroupBy(c => c.CharId).Where(g => g.Count() > 1).OrderBy(g => g.Key).Select(g =>
        {
            var copies = g.OrderByDescending(c => c.FittingId).ToList();
            var keep = copies.FirstOrDefault(c => FitSync.NameKey(c.Name) == key) ?? copies[0];
            return new TidyStep(g.Key, keep, copies.Where(c => c != keep).ToList());
        }).ToList();
    }

    /// <summary>Deletes each step's copies; one character's failure never stops the rest.</summary>
    public static async Task<List<FitSyncResult>> ExecuteTidy(IEnumerable<TidyStep> steps, Func<long, long, Task<(bool ok, string message)>> delete)
    {
        var results = new List<FitSyncResult>();
        foreach (var s in steps)
        {
            var deleted = 0;
            var failures = new List<string>();
            foreach (var d in s.Delete)
            {
                var (ok, msg) = await delete(s.CharId, d.FittingId);
                if (ok) deleted++; else failures.Add(msg);
            }
            results.Add(new FitSyncResult(s.CharId, FitSyncAction.Nothing, false, deleted, failures.Count == 0,
                failures.Count == 0 ? $"Deleted {deleted} duplicate{(deleted == 1 ? "" : "s")} · kept “{s.Keep.Name}”"
                    : $"Deleted {deleted} of {s.Delete.Count} · " + string.Join(" · ", failures)));
        }
        return results;
    }
}
