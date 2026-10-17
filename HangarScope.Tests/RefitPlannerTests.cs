using HangarScope.Models;
using HangarScope.Services;

namespace HangarScope.Tests;

public class RefitPlannerTests
{
    // characters, stations, types
    private const long Alice = 1, Bob = 2;
    private const long Crater = 100, Jita = 200, Amarr = 300;
    private const long Paladin = 28659, Laser = 3057, HeatSink = 2364, Mjd = 4383, Rig = 26082, Drone = 2456, Crystal = 23071;

    private static readonly Dictionary<long, TypeMeta> Types = new()
    {
        [Paladin] = new() { Name = "Paladin", Category = "Ships", Sell = 1.6e9, Buy = 1.5e9, Slots = new SlotLayout { High = 8, Med = 4, Low = 7, Rig = 2 } },
        [Laser] = new() { Name = "Mega Pulse Laser II", Category = "Modules", Sell = 3e6 },
        [HeatSink] = new() { Name = "Heat Sink II", Category = "Modules", Sell = 1e6 },
        [Mjd] = new() { Name = "Large Micro Jump Drive", Category = "Modules", Sell = 27e6 },
        [Rig] = new() { Name = "Large Energy Locus Coordinator II", Category = "Modules", Sell = 28e6 },
        [Drone] = new() { Name = "Hobgoblin II", Category = "Drones", Sell = 0.5e6 },
        [Crystal] = new() { Name = "Imperial Navy Multifrequency L", Category = "Ammunition", Sell = 2e6 },
    };

    private static PlanShip Ship(string key, long charId, params (long type, string flag, long qty)[] items) => new()
    {
        Key = key, ItemId = 1000 + key[0], TypeId = Paladin, Name = "Paladin " + key, CharId = charId, StationId = Crater,
        Items = items.Select(i => new FitItem { TypeId = i.type, Flag = i.flag, Qty = i.qty }).ToList(),
    };

    private static RefitTarget Target(params (string group, long type, long qty)[] lines) => new()
    {
        ShipTypeId = Paladin, Name = "Doctrine",
        Lines = lines.Select(l => new TargetLine { Group = l.group, TypeId = l.type, Qty = l.qty }).ToList(),
    };

    private static PlanOptions Opts(Action<PlanOptions>? f = null)
    {
        var o = new PlanOptions { Types = Types };
        f?.Invoke(o);
        return o;
    }

    private static StockStack Stock(long type, long qty, long charId, long station, string where = "Item hangar") =>
        new() { TypeId = type, Qty = qty, CharId = charId, StationId = station, Where = where };

    [Fact]
    public void ItemsFittedToUnselectedShips_AreNeverAvailable()
    {
        var unselected = new ShipInstance
        {
            ItemId = 9, TypeId = Paladin, Name = "Spare", CharId = Alice, StationId = Crater,
            Items = { new FitItem { TypeId = HeatSink, Flag = "LoSlot0", Qty = 1 } },
        };
        var assets = new List<AssetStack>
        {
            new() { TypeId = HeatSink, Qty = 1, CharId = Alice, StationId = Crater, Flag = OwnershipFlag.Fitted, Category = "Modules" },
        };
        var stock = RefitPlanner.BuildStock(assets, new[] { unselected });
        Assert.Empty(stock);

        var a = Ship("A", Alice);
        var plan = RefitPlanner.Plan(new[] { a }, Target(("LOW", HeatSink, 1)), stock, Opts());
        var fit = Assert.Single(plan.Orders["A"].Fit);
        Assert.Equal(FitSource.Buy, fit.Source);
    }

    [Fact]
    public void UnfittedStock_IsPickup_WithLocationOwnerAndJumps()
    {
        var assets = new List<AssetStack>
        {
            new() { TypeId = HeatSink, Qty = 4, CharId = Alice, StationId = Crater, Flag = OwnershipFlag.Container, Category = "Modules" },
        };
        var ship = new ShipInstance
        {
            ItemId = 7, TypeId = Paladin, Name = "Hauler", CharId = Alice, StationId = Crater,
            Items = { new FitItem { TypeId = HeatSink, Flag = "Cargo", Qty = 1 } },
        };
        var stock = RefitPlanner.BuildStock(assets, new[] { ship });
        // the cargo unit is listed against the ship, the rest against the container
        Assert.Contains(stock, s => s.Where == "Container" && s.Qty == 3);
        Assert.Contains(stock, s => s.Where == "Cargo · Hauler" && s.Qty == 1);

        var plan = RefitPlanner.Plan(new[] { Ship("A", Alice) }, Target(("LOW", HeatSink, 2)), stock, Opts());
        Assert.All(plan.Orders["A"].Fit, f => Assert.Equal(FitSource.Pickup, f.Source));
        Assert.Equal(2, plan.PickupCount);
        var p = plan.Pickups[0];
        Assert.Equal(Alice, p.Stack.CharId);
        Assert.Equal(Crater, p.Stack.StationId);
        Assert.Equal(0, p.Jumps);
        Assert.Empty(plan.Buys);
    }

    [Fact]
    public void SwapsBetweenSelectedShips_ComeBeforeStock()
    {
        var a = Ship("A", Alice, (HeatSink, "LoSlot0", 1), (HeatSink, "LoSlot1", 1));
        var b = Ship("B", Alice);
        var stock = new[] { Stock(HeatSink, 10, Alice, Crater) };
        var plan = RefitPlanner.Plan(new[] { a, b }, Target(("LOW", HeatSink, 1)), stock, Opts());

        Assert.Equal(new StripLine("LOW", HeatSink, 1, false), Assert.Single(plan.Orders["A"].Strip));
        var fit = Assert.Single(plan.Orders["B"].Fit);
        Assert.Equal(FitSource.Swap, fit.Source);
        Assert.Equal("A", fit.FromShipKey);
        Assert.Empty(plan.Pickups);
        Assert.Empty(plan.Leftovers);
    }

    [Fact]
    public void MovesBetweenCharacters_NeedAContract_AndCanBeDisabled()
    {
        var a = Ship("A", Alice, (Mjd, "MedSlot0", 1));
        var b = Ship("B", Bob);
        var stock = new[] { Stock(HeatSink, 1, Alice, Crater) };
        var target = Target(("LOW", HeatSink, 1));

        var plan = RefitPlanner.Plan(new[] { a, b }, Target(("MED", Mjd, 0)).Also(target), stock, Opts());
        var mjd = plan.Orders["B"].Fit.Where(f => f.TypeId == Mjd);
        Assert.Empty(mjd); // target has 0 MJDs, so A strips it into leftovers

        var swapPlan = RefitPlanner.Plan(new[] { Ship("A", Alice, (Mjd, "MedSlot0", 2)), b }, Target(("MED", Mjd, 1)), Array.Empty<StockStack>(), Opts());
        var c = Assert.Single(swapPlan.Orders["B"].Fit);
        Assert.Equal(FitSource.Contract, c.Source);

        var pick = RefitPlanner.Plan(new[] { b }, target, stock, Opts());
        var pf = Assert.Single(pick.Orders["B"].Fit);
        Assert.Equal(FitSource.Pickup, pf.Source);
        Assert.True(pf.NeedsContract);

        var off = Opts(o => o.AllowContracts = false);
        var noSwap = RefitPlanner.Plan(new[] { Ship("A", Alice, (Mjd, "MedSlot0", 2)), b }, Target(("MED", Mjd, 1)), Array.Empty<StockStack>(), off);
        Assert.Equal(FitSource.Buy, Assert.Single(noSwap.Orders["B"].Fit).Source);
        Assert.Single(noSwap.Leftovers);
        var noPick = RefitPlanner.Plan(new[] { b }, target, stock, off);
        Assert.Equal(FitSource.Buy, Assert.Single(noPick.Orders["B"].Fit).Source);
    }

    [Fact]
    public void Rigs_AreDestroyedOnRemoval_AndNeverSwapped()
    {
        var a = Ship("A", Alice, (Rig, "RigSlot0", 1));
        var b = Ship("B", Alice);
        var plan = RefitPlanner.Plan(new[] { a, b }, Target(), Array.Empty<StockStack>(), Opts());
        Assert.True(Assert.Single(plan.Orders["A"].Strip).Destroyed);
        Assert.Equal(1, plan.RigsDestroyed);
        Assert.Empty(plan.Leftovers);

        // B needs the rig A strips: it must be bought, not swapped
        var a2 = Ship("A", Alice, (Rig, "RigSlot0", 2));
        var plan2 = RefitPlanner.Plan(new[] { a2, b }, Target(("RIG", Rig, 1)), Array.Empty<StockStack>(), Opts());
        Assert.Equal(FitSource.Buy, Assert.Single(plan2.Orders["B"].Fit).Source);
        Assert.Empty(plan2.Leftovers);
        Assert.Contains(plan2.Rows, r => r.TypeId == Rig && r.Destroyed);
    }

    [Fact]
    public void NewHulls_AreFilledLast_AndTheHullIsBought()
    {
        var a = Ship("A", Alice, (Laser, "HiSlot0", 1), (Laser, "HiSlot1", 1), (Laser, "HiSlot2", 1));
        var b = Ship("B", Alice, (Laser, "HiSlot0", 1));
        var opts = Opts(o => o.NewHulls = new NewHullSpec { Count = 1, CharId = Alice, StationId = Crater });
        var plan = RefitPlanner.Plan(new[] { a, b }, Target(("HIGH", Laser, 2)), Array.Empty<StockStack>(), opts);

        Assert.Equal(3, plan.Ships.Count);
        Assert.True(plan.Ships[2].IsNew);
        Assert.Equal(FitSource.Swap, Assert.Single(plan.Orders["B"].Fit).Source);
        var n1 = Assert.Single(plan.Orders["N1"].Fit);
        Assert.Equal(FitSource.Buy, n1.Source);
        Assert.Equal(2, n1.Qty);
        Assert.Empty(plan.Orders["N1"].Strip);
        var hull = Assert.Single(plan.Buys, x => x.IsHull);
        Assert.Equal(Paladin, hull.TypeId);
        Assert.Equal(1.6e9 + 2 * 3e6, plan.BuyTotal);
    }

    [Fact]
    public void SlotCounts_AreCheckedPerHull()
    {
        var target = Target(("HIGH", Laser, 9), ("LOW", HeatSink, 3), ("RIG", Rig, 2));
        var plan = RefitPlanner.Plan(new[] { Ship("A", Alice) }, target, Array.Empty<StockStack>(), Opts());
        Assert.Equal(SlotStatus.Over, plan.SlotChecks.Single(s => s.Group == "HIGH").Status);
        Assert.Equal(SlotStatus.Under, plan.SlotChecks.Single(s => s.Group == "LOW").Status);
        Assert.Equal(SlotStatus.Ok, plan.SlotChecks.Single(s => s.Group == "RIG").Status);
        Assert.Equal(4, plan.SlotChecks.Single(s => s.Group == "MED").Cap);
    }

    [Fact]
    public void LoadedCharges_AreChargesNotModules()
    {
        // real ESI data: a loaded crystal sits under a HiSlot flag next to its laser
        var a = Ship("A", Alice, (Laser, "HiSlot0", 1), (Crystal, "HiSlot0", 1));
        Assert.Equal("CHARGES", RefitPlanner.GroupOf(a.Items[1], Types));

        var plan = RefitPlanner.Plan(new[] { a }, Target(("HIGH", Laser, 1)), Array.Empty<StockStack>(), Opts());
        Assert.Empty(plan.Orders["A"].Strip); // the crystal is not a stray high-slot module
        Assert.DoesNotContain(plan.Rows, r => r.TypeId == Crystal);
        Assert.Equal(1, plan.SlotChecks.Single(s => s.Group == "HIGH").Used);

        var withCharges = RefitPlanner.Plan(new[] { a }, Target(("HIGH", Laser, 1), ("CHARGES", Crystal, 2)), Array.Empty<StockStack>(),
            Opts(o => o.IncludeCharges = true));
        var row = withCharges.Rows.Single(r => r.TypeId == Crystal);
        Assert.Equal("CHARGES", row.Group);
        Assert.Equal(1, row.Current["A"]);
        Assert.Equal(1, plan.SlotChecks.Single(s => s.Group == "HIGH").Used);
    }

    [Fact]
    public void LoadedCharges_AreNotPickupStock()
    {
        var ship = new ShipInstance
        {
            ItemId = 5, TypeId = Paladin, CharId = Alice, StationId = Crater,
            Items = { new FitItem { TypeId = Crystal, Flag = "HiSlot0", Qty = 1 }, new FitItem { TypeId = Crystal, Flag = "Cargo", Qty = 8 } },
        };
        var stock = RefitPlanner.BuildStock(Array.Empty<AssetStack>(), new[] { ship });
        Assert.Equal(8, Assert.Single(stock).Qty);
    }

    [Fact]
    public void NearestStock_ThenOwnPilot_ThenBiggestStack()
    {
        var stock = new[]
        {
            Stock(HeatSink, 5, Alice, Jita),
            Stock(HeatSink, 1, Bob, Crater),
            Stock(HeatSink, 1, Alice, Crater),
        };
        var opts = Opts(o => o.Jumps = (from, to) => from == to ? 0 : from == Jita ? 40 : -1);
        var plan = RefitPlanner.Plan(new[] { Ship("A", Alice) }, Target(("LOW", HeatSink, 3)), stock, opts);
        var fits = plan.Orders["A"].Fit;
        Assert.Equal((Alice, Crater), (fits[0].FromCharId, fits[0].FromStationId));
        Assert.Equal((Bob, Crater), (fits[1].FromCharId, fits[1].FromStationId));
        Assert.True(fits[1].NeedsContract);
        Assert.Equal((Alice, Jita, 40), (fits[2].FromCharId, fits[2].FromStationId, fits[2].Jumps));
    }

    [Fact]
    public void Surplus_IsLeftOver_WithItsOwner()
    {
        var a = Ship("A", Bob, (HeatSink, "LoSlot0", 1), (HeatSink, "LoSlot1", 1));
        var plan = RefitPlanner.Plan(new[] { a }, Target(), Array.Empty<StockStack>(), Opts());
        var l = Assert.Single(plan.Leftovers);
        Assert.Equal((HeatSink, 2L, Bob, "A"), (l.TypeId, l.Qty, l.CharId, l.FromShipKey));
    }

    [Fact]
    public void Drones_FollowTheIncludeOption()
    {
        var a = Ship("A", Alice, (Drone, "DroneBay", 3));
        var with = RefitPlanner.Plan(new[] { a }, Target(("DRONES", Drone, 5)), Array.Empty<StockStack>(), Opts());
        Assert.Equal(2, Assert.Single(with.Buys).Qty);
        var without = RefitPlanner.Plan(new[] { a }, Target(("DRONES", Drone, 5)), Array.Empty<StockStack>(), Opts(o => o.IncludeDrones = false));
        Assert.Empty(without.Rows);
    }

    [Fact]
    public void PlanShipsOwnCargo_IsNotStock_WhenCargoIsCompared()
    {
        var a = Ship("A", Alice, (HeatSink, "Cargo", 2));
        var stock = new[] { new StockStack { TypeId = HeatSink, Qty = 2, CharId = Alice, StationId = Crater, Where = "Cargo · A", ShipItemId = a.ItemId } };
        var b = Ship("B", Alice);
        var plan = RefitPlanner.Plan(new[] { a, b }, Target(("LOW", HeatSink, 1)), stock, Opts(o => o.IncludeCargo = true));
        // A's cargo row exists (it is compared), the low-slot need is not met from A's hold
        Assert.Contains(plan.Rows, r => r.Group == "CARGO");
        Assert.DoesNotContain(plan.Orders["B"].Fit, f => f.Source == FitSource.Pickup);
    }

    [Fact]
    public void FitItems_RoundTripThroughTargetLines()
    {
        var lines = Target(("HIGH", Laser, 2), ("LOW", HeatSink, 1), ("DRONES", Drone, 5)).Lines;
        var items = RefitPlanner.ToFitItems(lines);
        Assert.Equal(new[] { "HiSlot0", "HiSlot1", "LoSlot0", "DroneBay" }, items.Select(i => i.Flag));
        var back = RefitPlanner.ToLines(items, Types);
        Assert.Equal(lines.Select(l => (l.Group, l.TypeId, l.Qty)).OrderBy(x => x), back.Select(l => (l.Group, l.TypeId, l.Qty)).OrderBy(x => x));
    }
}

internal static class TargetExt
{
    public static RefitTarget Also(this RefitTarget t, RefitTarget other)
    {
        t.Lines.AddRange(other.Lines);
        return t;
    }
}
