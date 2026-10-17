using HangarScope.Models;
using HangarScope.Services;

namespace HangarScope.Tests;

public class EftFormatTests
{
    private static readonly Dictionary<long, TypeMeta> Types = new()
    {
        [28659] = new() { Name = "Paladin", Category = "Ships" },
        [3057] = new() { Name = "Mega Pulse Laser II", Category = "Modules" },
        [2364] = new() { Name = "Heat Sink II", Category = "Modules" },
        [4383] = new() { Name = "Large Micro Jump Drive", Category = "Modules" },
        [26082] = new() { Name = "Large Energy Locus Coordinator II", Category = "Modules" },
        [2456] = new() { Name = "Hobgoblin II", Category = "Drones" },
        [23071] = new() { Name = "Imperial Navy Multifrequency L", Category = "Ammunition" },
        [28668] = new() { Name = "Nanite Repair Paste", Category = "Commodities" },
    };

    private static long? Resolve(string name) =>
        Types.FirstOrDefault(kv => kv.Value.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Key is var k && k != 0 ? k : null;

    private const string Sample = """
        [Paladin, Marauders IV No Dread]
        Heat Sink II
        Heat Sink II
        [Empty Low slot]

        Large Micro Jump Drive

        Mega Pulse Laser II, Imperial Navy Multifrequency L
        Mega Pulse Laser II, Imperial Navy Multifrequency L

        Large Energy Locus Coordinator II
        [Empty Rig slot]


        Hobgoblin II x5


        Imperial Navy Multifrequency L x8
        Nanite Repair Paste x50
        """;

    [Fact]
    public void Parse_ReadsSlotBlocksBaysAndLoadedCharges()
    {
        var r = EftFormat.Parse(Sample, Resolve, Types);
        Assert.Null(r.Error);
        Assert.Empty(r.Unresolved);
        Assert.Equal(28659, r.HullTypeId);
        Assert.Equal("Marauders IV No Dread", r.Name);
        long Q(string g, long t) => r.Lines.Where(l => l.Group == g && l.TypeId == t).Sum(l => l.Qty);
        Assert.Equal(2, Q("LOW", 2364));
        Assert.Equal(1, Q("MED", 4383));
        Assert.Equal(2, Q("HIGH", 3057));
        Assert.Equal(1, Q("RIG", 26082));
        Assert.Equal(5, Q("DRONES", 2456));
        Assert.Equal(10, Q("CHARGES", 23071)); // 2 loaded + 8 in cargo, never a module
        Assert.Equal(50, Q("CARGO", 28668));
    }

    [Fact]
    public void Parse_ReportsUnknownNames()
    {
        var r = EftFormat.Parse("[Paladin, x]\nMystery Module II\n", Resolve, Types);
        Assert.Equal(new[] { "Mystery Module II" }, r.Unresolved);
    }

    [Fact]
    public void Parse_RejectsTextWithoutHeader()
    {
        Assert.NotNull(EftFormat.Parse("Heat Sink II", Resolve, Types).Error);
    }

    [Fact]
    public void Export_RoundTrips()
    {
        var parsed = EftFormat.Parse(Sample, Resolve, Types);
        var target = new RefitTarget { ShipTypeId = 28659, Name = parsed.Name, Lines = parsed.Lines };
        var slots = new SlotLayout { High = 8, Med = 4, Low = 7, Rig = 2 };
        var text = EftFormat.Export(target, id => Types[id].Name, slots);
        Assert.StartsWith("[Paladin, Marauders IV No Dread]\n", text);
        Assert.Contains("[Empty High slot]", text);
        Assert.Contains("Hobgoblin II x5", text);
        var again = EftFormat.Parse(text, Resolve, Types);
        Assert.Equal(parsed.Lines.Select(l => (l.Group, l.TypeId, l.Qty)).OrderBy(x => x),
            again.Lines.Select(l => (l.Group, l.TypeId, l.Qty)).OrderBy(x => x));
    }

    [Fact]
    public void Multibuy_IsOneTabSeparatedLinePerType()
    {
        var text = EftFormat.Multibuy(new[] { new BuyLine(2364, 3, 1e6, false), new BuyLine(28659, 1, 1.6e9, true), new BuyLine(2364, 1, 1e6, false) },
            id => Types[id].Name);
        Assert.Equal("Heat Sink II\t4\nPaladin\t1", text);
    }
}
