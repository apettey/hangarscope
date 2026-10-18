using HangarScope.Models;
using HangarScope.Services;

namespace HangarScope.Tests;

public class FitCatalogTests
{
    private const long Paladin = 28659, Golem = 28710, Laser = 3057, HeatSink = 2364, Hobgoblin = 2456, Hammerhead = 2185, Crystal = 23071, Torp = 2420;

    private static readonly Dictionary<long, string> Names = new()
    {
        [Paladin] = "Paladin", [Golem] = "Golem", [Laser] = "Mega Pulse Laser II", [HeatSink] = "Heat Sink II",
        [Hobgoblin] = "Hobgoblin II", [Hammerhead] = "Hammerhead II", [Crystal] = "Imperial Navy Multifrequency L", [Torp] = "Torpedo Launcher II",
    };

    private static string TypeName(long id) => Names.TryGetValue(id, out var n) ? n : $"Type {id}";

    private static List<FitItem> Items(params (long type, string flag, long qty)[] items) =>
        items.Select(i => new FitItem { TypeId = i.type, Flag = i.flag, Qty = i.qty }).ToList();

    private static readonly List<FitItem> Base = Items((Laser, "HiSlot0", 1), (HeatSink, "LoSlot0", 1), (Hobgoblin, "DroneBay", 5));
    private static readonly List<FitItem> MoreCargo = Items((Laser, "HiSlot0", 1), (HeatSink, "LoSlot0", 1), (Hobgoblin, "DroneBay", 5), (Crystal, "Cargo", 8));
    private static readonly List<FitItem> OtherDrones = Items((Laser, "HiSlot0", 1), (HeatSink, "LoSlot0", 1), (Hammerhead, "DroneBay", 5));
    private static readonly List<FitItem> OtherModules = Items((Laser, "HiSlot0", 1), (HeatSink, "LoSlot0", 1), (HeatSink, "LoSlot1", 1));

    private static SavedFitting Fit(long id, long charId, string name, List<FitItem> items, long hull = Paladin) =>
        new() { FittingId = id, CharId = charId, Name = name, ShipTypeId = hull, Items = items };

    [Fact]
    public void Identical_fits_merge_with_every_name_and_pilot()
    {
        var g = FitCatalog.Group(new[]
        {
            Fit(1, 1, "Blitz", Base), Fit(2, 2, "Blitz", Base), Fit(3, 3, "Blitz copy", Base), Fit(4, 1, "Other", OtherModules),
        });
        Assert.Equal(2, g.Count);
        var blitz = g.Single(x => x.Copies.Count == 3);
        Assert.Equal(new[] { "Blitz", "Blitz copy" }, blitz.Names);   // most used name first
        Assert.Equal(3, blitz.PilotCount);
        Assert.Equal(3, blitz.NewestId);
    }

    [Fact]
    public void Families_join_cargo_and_drone_variants_and_same_names_but_not_other_hulls()
    {
        var families = FitCatalog.Families(FitCatalog.Group(new[]
        {
            Fit(1, 1, "Blitz", Base), Fit(2, 2, "Blitz", Base),
            Fit(3, 3, "Blitz hauler", MoreCargo),            // same slots, cargo differs
            Fit(4, 3, "Blitz hobs", OtherDrones),            // same slots, drones differ
            Fit(5, 1, " blitz ", OtherModules),              // same name, modules differ
            Fit(6, 1, "Blitz", Base, hull: Golem),           // same name on another hull: separate
            Fit(7, 2, "Unrelated", OtherModules, hull: Golem),
        }));
        var paladin = families.Single(f => f.ShipTypeId == Paladin);
        Assert.Equal(4, paladin.Groups.Count);
        Assert.Equal("Blitz", paladin.Main.Name);       // main = most pilots
        Assert.Equal(FitDiff.Modules | FitDiff.Drones | FitDiff.Cargo, paladin.Diff);
        Assert.Equal("4 variants · modules, drones, cargo differ", FitCatalog.VariantLabel(paladin));
        Assert.Equal(2, families.Count(f => f.ShipTypeId == Golem));
        Assert.All(families.Where(f => f.ShipTypeId == Golem), f => Assert.Equal("", FitCatalog.VariantLabel(f)));
    }

    [Fact]
    public void Diff_names_the_kind_of_change()
    {
        Assert.Equal(FitDiff.None, FitCatalog.Diff(Base, Base));
        Assert.Equal(FitDiff.Cargo, FitCatalog.Diff(Base, MoreCargo));
        Assert.Equal(FitDiff.Drones, FitCatalog.Diff(Base, OtherDrones));
        Assert.Equal(FitDiff.Modules | FitDiff.Drones, FitCatalog.Diff(Base, OtherModules));
        Assert.Equal(new[] { ("LOW", HeatSink, 1L), ("DRONES", Hobgoblin, -5L) }, FitCatalog.Deltas(Base, OtherModules));
    }

    [Fact]
    public void Search_ranks_name_over_hull_over_items_and_requires_every_word()
    {
        var families = FitCatalog.Families(FitCatalog.Group(new[]
        {
            Fit(1, 1, "Heat ratter", OtherModules, hull: Golem),      // name match
            Fit(2, 1, "Laser boat", Base),                            // item match only (Heat Sink II)
            Fit(3, 1, "Torp golem", Items((Torp, "HiSlot0", 1)), hull: Golem),
        }));
        var all = new HashSet<long>();
        var r = FitCatalog.Search(families, "heat", all, FitSort.Name, TypeName);
        Assert.Equal(new[] { "Heat ratter", "Laser boat" }, r.Select(f => f.Main.Name));

        Assert.Equal(new[] { "Heat ratter", "Torp golem" }, FitCatalog.Search(families, "golem", all, FitSort.Name, TypeName).Select(f => f.Main.Name).OrderBy(x => x));
        Assert.Empty(FitCatalog.Search(families, "heat torpedo", all, FitSort.Name, TypeName));
        Assert.Equal(new[] { "Torp golem" }, FitCatalog.Search(families, "torpedo launcher", all, FitSort.Name, TypeName).Select(f => f.Main.Name));
    }

    [Fact]
    public void Hull_filter_and_sorts()
    {
        var families = FitCatalog.Families(FitCatalog.Group(new[]
        {
            Fit(10, 1, "B", Base), Fit(11, 2, "B", Base),
            Fit(30, 1, "A", OtherModules, hull: Golem),
            Fit(20, 1, "C", Items((Torp, "HiSlot0", 1))),
        }));
        var none = new HashSet<long>();
        Assert.Equal(new[] { "A", "B", "C" }, FitCatalog.Search(families, "", none, FitSort.Name, TypeName).Select(f => f.Main.Name));
        Assert.Equal("B", FitCatalog.Search(families, "", none, FitSort.Pilots, TypeName)[0].Main.Name);
        Assert.Equal(new[] { "A", "C", "B" }, FitCatalog.Search(families, "", none, FitSort.Newest, TypeName).Select(f => f.Main.Name));
        Assert.Equal(new[] { "B", "C" }, FitCatalog.Search(families, "", new HashSet<long> { Paladin }, FitSort.Name, TypeName).Select(f => f.Main.Name));
    }

    [Fact]
    public void Tidy_keeps_the_chosen_name_and_deletes_only_identical_copies()
    {
        var groups = FitCatalog.Group(new[]
        {
            Fit(1, 1, "Blitz", Base), Fit(2, 1, "Blitz copy", Base), Fit(3, 1, "Blitz", Base),   // pilot 1: three identical copies
            Fit(4, 2, "Blitz copy", Base), Fit(5, 2, "Old", Base),                               // pilot 2: no copy named "Blitz"
            Fit(6, 3, "Blitz", Base),                                                            // pilot 3: one copy, nothing to tidy
            Fit(7, 1, "Blitz", MoreCargo),                                                       // variant: never touched
        });
        var steps = FitCatalog.PlanTidy(groups.Single(g => g.Hash == FitMath.Hash(Paladin, Base)), "Blitz");
        Assert.Equal(new long[] { 1, 2 }, steps.Select(s => s.CharId));
        Assert.Equal(3, steps[0].Keep.FittingId);                                    // newest copy with the chosen name
        Assert.Equal(new long[] { 2, 1 }, steps[0].Delete.Select(d => d.FittingId));
        Assert.Equal(5, steps[1].Keep.FittingId);                                    // no "Blitz" copy: newest is kept
        Assert.Equal(new long[] { 4 }, steps[1].Delete.Select(d => d.FittingId));
        Assert.DoesNotContain(steps.SelectMany(s => s.Delete), d => d.FittingId == 7);
    }

    [Fact]
    public async Task Tidy_execution_continues_past_failures()
    {
        var keep = Fit(1, 1, "Blitz", Base);
        var calls = new List<long>();
        var r = await FitCatalog.ExecuteTidy(new[]
        {
            new TidyStep(1, keep, new[] { Fit(2, 1, "x", Base), Fit(3, 1, "y", Base) }),
            new TidyStep(2, Fit(4, 2, "Blitz", Base), new[] { Fit(5, 2, "z", Base) }),
        }, (c, id) => { calls.Add(id); return Task.FromResult(id == 2 ? (false, "ESI 500") : (true, "ok")); });
        Assert.Equal(new long[] { 2, 3, 5 }, calls);
        Assert.False(r[0].Ok);
        Assert.Equal(1, r[0].Deleted);
        Assert.True(r[1].Ok);
    }
}
