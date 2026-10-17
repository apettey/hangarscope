using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HangarScope.Models;

namespace HangarScope.Services;

/// <summary>
/// The in-game "Copy to clipboard" EFT fitting format, plus the multibuy list format.
/// Blocks are separated by blank lines in the order low, med, high, rig, subsystem, then drones and cargo.
/// </summary>
public static partial class EftFormat
{
    public sealed class ParseResult
    {
        public string HullName { get; set; } = "";
        public long? HullTypeId { get; set; }
        public string Name { get; set; } = "";
        public List<TargetLine> Lines { get; } = new();
        /// <summary>Type names the resolver did not know; resolve them (POST /universe/ids/) and parse again.</summary>
        public List<string> Unresolved { get; } = new();
        public string? Error { get; set; }
    }

    private static readonly string[] SlotSequence = { "LOW", "MED", "HIGH", "RIG", "SUBSYSTEM" };

    [GeneratedRegex(@"^\[(?<hull>[^,\]]+)(,\s*(?<name>.*))?\]$")]
    private static partial Regex HeaderRx();

    [GeneratedRegex(@"^\[Empty (?<slot>\w+) slot\]$", RegexOptions.IgnoreCase)]
    private static partial Regex EmptyRx();

    [GeneratedRegex(@"^(?<name>.+?)\s+x(?<qty>\d+)$")]
    private static partial Regex QtyRx();

    public static ParseResult Parse(string text, Func<string, long?> resolve, IReadOnlyDictionary<long, TypeMeta> types)
    {
        var r = new ParseResult();
        var lines = text.Replace("\r", "").Split('\n').Select(l => l.Trim()).ToList();
        var first = lines.FindIndex(l => l.Length > 0);
        if (first < 0) { r.Error = "Nothing to import."; return r; }
        var head = HeaderRx().Match(lines[first]);
        if (!head.Success) { r.Error = "Expected an EFT header like [Paladin, My fit]."; return r; }
        r.HullName = head.Groups["hull"].Value.Trim();
        r.Name = head.Groups["name"].Success ? head.Groups["name"].Value.Trim() : "";
        r.HullTypeId = resolve(r.HullName);
        if (r.HullTypeId == null) r.Unresolved.Add(r.HullName);

        // split the body into blank-line separated blocks
        var blocks = new List<List<string>>();
        List<string>? cur = null;
        foreach (var l in lines.Skip(first + 1))
        {
            if (l.Length == 0) { cur = null; continue; }
            if (cur == null) blocks.Add(cur = new List<string>());
            cur.Add(l);
        }

        var acc = new Dictionary<(string g, long t), long>();
        void Add(string g, string name, long qty)
        {
            var id = resolve(name);
            if (id == null) { if (!r.Unresolved.Contains(name)) r.Unresolved.Add(name); return; }
            var group = RefitPlanner.IsCharge(types, id.Value) ? "CHARGES" : g;
            acc[(group, id.Value)] = acc.GetValueOrDefault((group, id.Value)) + qty;
        }

        var slotIdx = 0;
        foreach (var block in blocks)
        {
            string? explicitGroup = null;
            foreach (var l in block)
            {
                var e = EmptyRx().Match(l);
                if (e.Success) explicitGroup ??= SlotFromWord(e.Groups["slot"].Value);
            }
            var isBay = explicitGroup == null && block.All(l => QtyRx().IsMatch(l));
            string moduleGroup = "CARGO";
            if (!isBay)
            {
                moduleGroup = explicitGroup ?? SlotSequence[Math.Min(slotIdx, SlotSequence.Length - 1)];
                slotIdx = Array.IndexOf(SlotSequence, moduleGroup) + 1;
            }

            foreach (var raw in block)
            {
                if (EmptyRx().IsMatch(raw)) continue;
                var l = raw.EndsWith("/offline", StringComparison.OrdinalIgnoreCase) ? raw[..^8].Trim() : raw;
                var q = QtyRx().Match(l);
                if (q.Success)
                {
                    var name = q.Groups["name"].Value.Trim();
                    var qty = long.Parse(q.Groups["qty"].Value, CultureInfo.InvariantCulture);
                    var id = resolve(name);
                    var cat = id is { } tid ? types.GetValueOrDefault(tid)?.Category : null;
                    Add(cat == "Drones" ? "DRONES" : "CARGO", name, qty);
                    continue;
                }
                var comma = l.IndexOf(", ", StringComparison.Ordinal);
                var module = comma > 0 ? l[..comma].Trim() : l;
                Add(moduleGroup, module, 1);
                if (comma > 0) Add("CHARGES", l[(comma + 2)..].Trim(), 1); // loaded charge
            }
        }

        r.Lines.AddRange(acc.Select(kv => new TargetLine { Group = kv.Key.g, TypeId = kv.Key.t, Qty = kv.Value })
            .OrderBy(l => Array.IndexOf(RefitPlanner.GroupOrder, l.Group)).ThenBy(l => l.TypeId));
        return r;
    }

    private static string? SlotFromWord(string w) => w.ToLowerInvariant() switch
    {
        "low" => "LOW", "med" or "medium" or "mid" => "MED", "high" => "HIGH", "rig" => "RIG", "subsystem" => "SUBSYSTEM",
        _ => null,
    };

    /// <summary>EFT text for a target fit. Empty slots are written out when the hull's slot layout is known.</summary>
    public static string Export(RefitTarget target, Func<long, string> name, SlotLayout? slots = null)
    {
        var sb = new StringBuilder();
        sb.Append('[').Append(name(target.ShipTypeId)).Append(", ").Append(string.IsNullOrWhiteSpace(target.Name) ? "HangarScope target" : target.Name).Append("]\n");
        var lines = target.Lines.Where(l => l.Qty > 0).ToList();
        foreach (var g in SlotSequence)
        {
            var mods = lines.Where(l => l.Group == g).ToList();
            var used = 0L;
            var block = new StringBuilder();
            foreach (var m in mods)
                for (var i = 0; i < m.Qty; i++) { block.Append(name(m.TypeId)).Append('\n'); used++; }
            var cap = slots?.For(g) ?? 0;
            for (var i = used; i < cap; i++) block.Append("[Empty ").Append(EmptyWord(g)).Append(" slot]\n");
            if (block.Length == 0) continue;
            sb.Append('\n').Append(block);
        }
        foreach (var bay in new[] { new[] { "DRONES" }, new[] { "FIGHTERS" }, new[] { "CHARGES", "CARGO" } })
        {
            var items = lines.Where(l => bay.Contains(l.Group)).ToList();
            if (items.Count == 0) continue;
            sb.Append('\n');
            foreach (var i in items) sb.Append(name(i.TypeId)).Append(" x").Append(i.Qty.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
        return sb.ToString();
    }

    private static string EmptyWord(string g) => g switch
    {
        "LOW" => "Low", "MED" => "Med", "HIGH" => "High", "RIG" => "Rig", _ => "Subsystem",
    };

    /// <summary>The in-game Multibuy window accepts one "name&lt;tab&gt;qty" per line.</summary>
    public static string Multibuy(IEnumerable<BuyLine> buys, Func<long, string> name) =>
        string.Join("\n", buys.GroupBy(b => b.TypeId)
            .Select(g => name(g.Key) + "\t" + g.Sum(b => b.Qty).ToString(CultureInfo.InvariantCulture)));
}
