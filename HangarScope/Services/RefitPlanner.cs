using HangarScope.Models;

namespace HangarScope.Services;

// ---------------------------------------------------------------------------------------------
// REFIT planner: given the ships to standardise, one target fit and the unfitted stock, work out
// for every hull what to strip and what to fit, where each fitted item comes from, and what is
// left to pick up, buy or keep. Pure: no I/O, no UI, dictionaries keyed by type throughout.
// ---------------------------------------------------------------------------------------------

public enum FitSource { Swap, Contract, Pickup, Buy }

/// <summary>One line of the target fit: a type in a slot group, and how many of it every ship should carry.</summary>
public sealed class TargetLine
{
    public string Group { get; set; } = "";
    public long TypeId { get; set; }
    public long Qty { get; set; }
}

public sealed class RefitTarget
{
    public long ShipTypeId { get; set; }
    public string Name { get; set; } = "";
    public List<TargetLine> Lines { get; set; } = new();

    public long QtyOf(string group, long typeId) =>
        Lines.Where(l => l.Group == group && l.TypeId == typeId).Sum(l => l.Qty);
}

/// <summary>A ship taking part in the plan: an existing hull, or a new empty one to buy.</summary>
public sealed class PlanShip
{
    /// <summary>Short column key: A, B, C… for existing ships, N1, N2… for new hulls.</summary>
    public string Key { get; set; } = "";
    public long ItemId { get; set; }
    public long TypeId { get; set; }
    public string Name { get; set; } = "";
    public long CharId { get; set; }
    public long StationId { get; set; }
    public bool IsNew { get; set; }
    public List<FitItem> Items { get; set; } = new();
}

/// <summary>Unfitted items somewhere: a hangar stack, a container, or a ship's cargo hold.</summary>
public sealed class StockStack
{
    public long TypeId { get; set; }
    public long Qty { get; set; }
    public long CharId { get; set; }
    public long StationId { get; set; }
    /// <summary>"Item hangar", "Container", or "Cargo · {ship}".</summary>
    public string Where { get; set; } = "";
    /// <summary>Set when the stack sits in a ship's cargo; that ship's cargo is skipped when the plan compares cargo.</summary>
    public long? ShipItemId { get; set; }
}

public sealed class NewHullSpec
{
    public int Count { get; set; }
    public long CharId { get; set; }
    public long StationId { get; set; }
}

public sealed class PlanOptions
{
    /// <summary>Modules stripped from the selected ships fill gaps on the others first.</summary>
    public bool UseSwaps { get; set; } = true;
    /// <summary>Unfitted stock in hangars, containers and cargo.</summary>
    public bool UseStock { get; set; } = true;
    /// <summary>Moves between different characters (needs an item-exchange contract). Off = buy instead.</summary>
    public bool AllowContracts { get; set; } = true;
    public bool IncludeDrones { get; set; } = true;
    public bool IncludeCharges { get; set; }
    public bool IncludeCargo { get; set; }
    public string PriceSide { get; set; } = "sell";
    public NewHullSpec NewHulls { get; set; } = new();
    public IReadOnlyDictionary<long, TypeMeta> Types { get; set; } = new Dictionary<long, TypeMeta>();
    /// <summary>Jumps between two locations, −1 when unknown. Default: 0 for the same location, else unknown.</summary>
    public Func<long, long, int> Jumps { get; set; } = (a, b) => a == b ? 0 : -1;
}

public sealed record StripLine(string Group, long TypeId, long Qty, bool Destroyed);

public sealed record FitLine(string Group, long TypeId, long Qty, FitSource Source,
    string? FromShipKey, long FromCharId, long FromStationId, string Where, int Jumps)
{
    /// <summary>A pickup owned by another character also needs a contract.</summary>
    public bool NeedsContract { get; init; }
}

public sealed class WorkOrder
{
    public required PlanShip Ship { get; init; }
    public List<StripLine> Strip { get; } = new();
    public List<FitLine> Fit { get; } = new();
}

public sealed record PickupLine(long TypeId, long Qty, StockStack Stack, string ToShipKey, long ToCharId, bool NeedsContract, int Jumps);

public sealed record BuyLine(long TypeId, long Qty, double UnitPrice, bool IsHull)
{
    public double Total => Qty * UnitPrice;
}

public sealed record LeftoverLine(string Group, long TypeId, long Qty, string FromShipKey, long CharId, long StationId);

public enum SlotStatus { Ok, Under, Over, Unknown }

public sealed record SlotCheck(long HullTypeId, string Group, long Used, int? Cap)
{
    public SlotStatus Status => Cap is not { } c ? SlotStatus.Unknown : Used > c ? SlotStatus.Over : Used < c ? SlotStatus.Under : SlotStatus.Ok;
}

public sealed class MatrixRow
{
    public string Group { get; init; } = "";
    public long TypeId { get; init; }
    public long Target { get; init; }
    /// <summary>Current qty per ship key.</summary>
    public Dictionary<string, long> Current { get; } = new();
    public HashSet<FitSource> Sources { get; } = new();
    public bool Destroyed { get; set; }
    public long LeftOver { get; set; }
    public double BuyCost { get; set; }
}

public sealed class RefitPlan
{
    public List<PlanShip> Ships { get; } = new();
    public List<MatrixRow> Rows { get; } = new();
    public Dictionary<string, WorkOrder> Orders { get; } = new();
    public List<PickupLine> Pickups { get; } = new();
    public List<BuyLine> Buys { get; } = new();
    public List<LeftoverLine> Leftovers { get; } = new();
    public List<SlotCheck> SlotChecks { get; } = new();

    public double BuyTotal => Buys.Sum(b => b.Total);
    public long StripCount => Orders.Values.Sum(o => o.Strip.Sum(s => s.Qty));
    public long RigsDestroyed => Orders.Values.Sum(o => o.Strip.Where(s => s.Destroyed).Sum(s => s.Qty));
    public long MovedBetweenShips => Orders.Values.Sum(o => o.Fit.Where(f => f.Source is FitSource.Swap or FitSource.Contract).Sum(f => f.Qty));
    public long PickupCount => Pickups.Sum(p => p.Qty);
    public long ItemsToBuy => Buys.Where(b => !b.IsHull).Sum(b => b.Qty);
    public long HullsToBuy => Buys.Where(b => b.IsHull).Sum(b => b.Qty);
}

public static class RefitPlanner
{
    /// <summary>Order slot groups appear in the matrix and in EFT.</summary>
    public static readonly string[] GroupOrder = { "HIGH", "MED", "LOW", "RIG", "SUBSYSTEM", "DRONES", "FIGHTERS", "CHARGES", "CARGO" };
    private static readonly string[] SlotGroups = { "HIGH", "MED", "LOW", "RIG", "SUBSYSTEM" };

    public static bool IsCharge(IReadOnlyDictionary<long, TypeMeta> types, long typeId) =>
        types.TryGetValue(typeId, out var t) && t.IsCharge;

    public static string GroupOf(FitItem item, IReadOnlyDictionary<long, TypeMeta> types) =>
        FitMath.PlanGroup(item.Flag, IsCharge(types, item.TypeId));

    /// <summary>Whether a slot group takes part in the comparison under the given options.</summary>
    public static bool Compared(string group, PlanOptions o) => group switch
    {
        "DRONES" or "FIGHTERS" => o.IncludeDrones,
        "CHARGES" => o.IncludeCharges,
        "CARGO" => o.IncludeCargo,
        _ => true,
    };

    /// <summary>Collapses a list of fitted items into target lines (slot group + type → qty).</summary>
    public static List<TargetLine> ToLines(IEnumerable<FitItem> items, IReadOnlyDictionary<long, TypeMeta> types)
    {
        var acc = new Dictionary<(string g, long t), long>();
        foreach (var i in items)
        {
            var k = (GroupOf(i, types), i.TypeId);
            acc[k] = acc.GetValueOrDefault(k) + i.Qty;
        }
        return acc.Select(kv => new TargetLine { Group = kv.Key.g, TypeId = kv.Key.t, Qty = kv.Value })
            .OrderBy(l => Array.IndexOf(GroupOrder, l.Group)).ThenBy(l => l.TypeId).ToList();
    }

    /// <summary>Turns target lines back into ESI fitting items with concrete slot flags (HiSlot0, HiSlot1…).</summary>
    public static List<FitItem> ToFitItems(IEnumerable<TargetLine> lines)
    {
        var next = new Dictionary<string, int>();
        var items = new List<FitItem>();
        foreach (var l in lines.Where(l => l.Qty > 0).OrderBy(l => Array.IndexOf(GroupOrder, l.Group)))
        {
            var prefix = l.Group switch
            {
                "HIGH" => "HiSlot", "MED" => "MedSlot", "LOW" => "LoSlot", "RIG" => "RigSlot", "SUBSYSTEM" => "SubSystemSlot",
                _ => null,
            };
            if (prefix == null)
            {
                var bay = l.Group switch { "DRONES" => "DroneBay", "FIGHTERS" => "FighterBay", _ => "Cargo" };
                items.Add(new FitItem { TypeId = l.TypeId, Flag = bay, Qty = l.Qty });
                continue;
            }
            for (var i = 0; i < l.Qty; i++)
            {
                var n = next.GetValueOrDefault(prefix);
                next[prefix] = n + 1;
                items.Add(new FitItem { TypeId = l.TypeId, Flag = prefix + n, Qty = 1 });
            }
        }
        return items;
    }

    /// <summary>
    /// Unfitted stock from the per-station asset stacks plus every ship's cargo. Fitted stacks (modules,
    /// loaded charges, drone bays) are never stock: something fitted to a ship that is not in the plan stays put.
    /// </summary>
    public static List<StockStack> BuildStock(IEnumerable<AssetStack> assets, IEnumerable<ShipInstance> ships)
    {
        var stock = new List<StockStack>();
        // ship cargo is part of the station's Container stacks; take it out there and list it per ship instead
        var inCargo = new Dictionary<(long c, long s, long t), long>();
        var cargo = new List<StockStack>();
        foreach (var ship in ships)
        {
            foreach (var i in ship.Items)
            {
                if (FitMath.IsFittedFlag(i.Flag)) continue;
                var k = (ship.CharId, ship.StationId, i.TypeId);
                inCargo[k] = inCargo.GetValueOrDefault(k) + i.Qty;
                cargo.Add(new StockStack
                {
                    TypeId = i.TypeId, Qty = i.Qty, CharId = ship.CharId, StationId = ship.StationId,
                    Where = "Cargo · " + (ship.Name ?? "ship"), ShipItemId = ship.ItemId,
                });
            }
        }
        foreach (var a in assets)
        {
            if (a.Flag == OwnershipFlag.Fitted || a.Category == "Ships") continue;
            var qty = a.Qty;
            if (a.Flag == OwnershipFlag.Container)
            {
                var k = (a.CharId, a.StationId, a.TypeId);
                var c = Math.Min(qty, inCargo.GetValueOrDefault(k));
                qty -= c;
                if (c > 0) inCargo[k] -= c;
            }
            if (qty <= 0) continue;
            stock.Add(new StockStack
            {
                TypeId = a.TypeId, Qty = qty, CharId = a.CharId, StationId = a.StationId,
                Where = a.Flag == OwnershipFlag.Container ? "Container" : "Item hangar",
            });
        }
        stock.AddRange(cargo);
        return stock;
    }

    public static RefitPlan Plan(IReadOnlyList<PlanShip> ships, RefitTarget target, IEnumerable<StockStack> stock, PlanOptions o)
    {
        var plan = new RefitPlan();
        var types = o.Types;
        var hullType = target.ShipTypeId != 0 ? target.ShipTypeId : ships.FirstOrDefault()?.TypeId ?? 0;

        plan.Ships.AddRange(ships);
        for (var i = 0; i < Math.Max(0, o.NewHulls.Count); i++)
        {
            plan.Ships.Add(new PlanShip
            {
                Key = "N" + (i + 1), TypeId = hullType, IsNew = true, CharId = o.NewHulls.CharId,
                StationId = o.NewHulls.StationId != 0 ? o.NewHulls.StationId : ships.FirstOrDefault()?.StationId ?? 0,
                Name = "New " + (types.GetValueOrDefault(hullType)?.Name ?? "hull") + " " + (i + 1),
            });
        }
        foreach (var s in plan.Ships) plan.Orders[s.Key] = new WorkOrder { Ship = s };

        // current contents of each ship, keyed by (slot group, type)
        var current = new Dictionary<string, Dictionary<(string g, long t), long>>();
        foreach (var s in plan.Ships)
        {
            var d = new Dictionary<(string, long), long>();
            foreach (var i in s.Items)
            {
                var g = GroupOf(i, types);
                if (!Compared(g, o)) continue;
                d[(g, i.TypeId)] = d.GetValueOrDefault((g, i.TypeId)) + i.Qty;
            }
            current[s.Key] = d;
        }

        var want = new Dictionary<(string g, long t), long>();
        foreach (var l in target.Lines)
            if (Compared(l.Group, o) && l.Qty > 0)
                want[(l.Group, l.TypeId)] = want.GetValueOrDefault((l.Group, l.TypeId)) + l.Qty;

        // the plan's own ships' cargo is not stock when cargo is being compared
        var planShipIds = plan.Ships.Where(s => !s.IsNew).Select(s => s.ItemId).ToHashSet();
        var stockByType = new Dictionary<long, List<StockStack>>();
        if (o.UseStock)
        {
            foreach (var st in stock)
            {
                if (st.Qty <= 0) continue;
                if (st.ShipItemId is { } sid && planShipIds.Contains(sid) &&
                    Compared(IsCharge(types, st.TypeId) ? "CHARGES" : "CARGO", o)) continue;
                if (!stockByType.TryGetValue(st.TypeId, out var list)) stockByType[st.TypeId] = list = new List<StockStack>();
                list.Add(new StockStack { TypeId = st.TypeId, Qty = st.Qty, CharId = st.CharId, StationId = st.StationId, Where = st.Where, ShipItemId = st.ShipItemId });
            }
        }

        double Price(long t) => types.TryGetValue(t, out var m) ? (o.PriceSide == "buy" ? m.Buy : m.Sell) : 0;

        var keys = want.Keys.Concat(current.Values.SelectMany(d => d.Keys)).Distinct()
            .OrderBy(k => Array.IndexOf(GroupOrder, k.g))
            .ThenBy(k => types.GetValueOrDefault(k.t)?.Name ?? "", StringComparer.OrdinalIgnoreCase).ThenBy(k => k.t)
            .ToList();
        var buys = new Dictionary<long, long>();

        foreach (var key in keys)
        {
            var (g, t) = key;
            var w = want.GetValueOrDefault(key);
            var rig = g == "RIG";
            var row = new MatrixRow { Group = g, TypeId = t, Target = w };
            plan.Rows.Add(row);

            var pool = new List<(PlanShip ship, long qty)>();
            var needs = new List<(PlanShip ship, long qty)>();
            foreach (var s in plan.Ships) // existing ships first, new hulls last
            {
                var cur = current[s.Key].GetValueOrDefault(key);
                row.Current[s.Key] = cur;
                if (cur > w)
                {
                    plan.Orders[s.Key].Strip.Add(new StripLine(g, t, cur - w, rig));
                    if (rig) row.Destroyed = true; // removing a rig destroys it: never pooled, never left over
                    else pool.Add((s, cur - w));
                }
                else if (cur < w) needs.Add((s, w - cur));
            }

            var stacks = stockByType.GetValueOrDefault(t);
            foreach (var (ship, needQty) in needs)
            {
                var q = needQty;
                var order = plan.Orders[ship.Key];

                if (o.UseSwaps)
                {
                    foreach (var idx in Enumerable.Range(0, pool.Count)
                                 .OrderBy(i => pool[i].ship.CharId == ship.CharId ? 0 : 1)
                                 .ThenBy(i => pool[i].ship.StationId == ship.StationId ? 0 : 1).ToList())
                    {
                        if (q == 0) break;
                        var (from, avail) = pool[idx];
                        if (avail == 0) continue;
                        var sameChar = from.CharId == ship.CharId;
                        if (!sameChar && !o.AllowContracts) continue;
                        var n = Math.Min(q, avail);
                        pool[idx] = (from, avail - n);
                        q -= n;
                        var src = sameChar ? FitSource.Swap : FitSource.Contract;
                        order.Fit.Add(new FitLine(g, t, n, src, from.Key, from.CharId, from.StationId,
                            "from " + from.Name, o.Jumps(from.StationId, ship.StationId)));
                        row.Sources.Add(src);
                    }
                }

                if (q > 0 && stacks != null)
                {
                    var ranked = stacks
                        .Select(st => (st, j: o.Jumps(st.StationId, ship.StationId)))
                        .OrderBy(x => x.j < 0 ? int.MaxValue : x.j)
                        .ThenBy(x => x.st.CharId == ship.CharId ? 0 : 1)
                        .ThenByDescending(x => x.st.Qty).ToList();
                    foreach (var (st, j) in ranked)
                    {
                        if (q == 0) break;
                        if (st.Qty == 0) continue;
                        var contract = st.CharId != ship.CharId;
                        if (contract && !o.AllowContracts) continue;
                        var n = Math.Min(q, st.Qty);
                        st.Qty -= n;
                        q -= n;
                        order.Fit.Add(new FitLine(g, t, n, FitSource.Pickup, null, st.CharId, st.StationId, st.Where, j) { NeedsContract = contract });
                        plan.Pickups.Add(new PickupLine(t, n, st, ship.Key, ship.CharId, contract, j));
                        row.Sources.Add(FitSource.Pickup);
                    }
                }

                if (q > 0)
                {
                    order.Fit.Add(new FitLine(g, t, q, FitSource.Buy, null, 0, 0, "buy", -1));
                    buys[t] = buys.GetValueOrDefault(t) + q;
                    row.Sources.Add(FitSource.Buy);
                    row.BuyCost += q * Price(t);
                }
            }

            foreach (var (from, left) in pool)
            {
                if (left == 0) continue;
                plan.Leftovers.Add(new LeftoverLine(g, t, left, from.Key, from.CharId, from.StationId));
                row.LeftOver += left;
            }
        }

        var newCount = plan.Ships.Count(s => s.IsNew);
        if (newCount > 0 && hullType != 0) plan.Buys.Add(new BuyLine(hullType, newCount, Price(hullType), IsHull: true));
        plan.Buys.AddRange(buys.Select(kv => new BuyLine(kv.Key, kv.Value, Price(kv.Key), IsHull: false))
            .OrderByDescending(b => b.Total).ThenBy(b => b.TypeId));

        // slot-count check: the target applies to every hull in the plan
        foreach (var hull in plan.Ships.Select(s => s.TypeId).Append(hullType).Where(h => h != 0).Distinct())
        {
            var slots = types.GetValueOrDefault(hull)?.Slots;
            foreach (var g in SlotGroups)
            {
                var used = want.Where(kv => kv.Key.g == g).Sum(kv => kv.Value);
                var cap = slots?.For(g);
                if (used == 0 && (cap ?? 0) == 0) continue;
                plan.SlotChecks.Add(new SlotCheck(hull, g, used, cap));
            }
        }
        return plan;
    }
}
