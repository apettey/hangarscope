using HangarScope.Models;
using HangarScope.Services;

namespace HangarScope.Tests;

public class AssetLocatorTests
{
    private const long Station = 60003760, System = 30000142;
    private const long Paladin = 28659, Ssc = 3467, Rorqual = 28352, Laser = 3057, Crystal = 12824, Plex = 44992, Hob = 2456;

    private static readonly Dictionary<long, string> TypeNames = new()
    {
        [Paladin] = "Paladin", [Ssc] = "Small Secure Container", [Rorqual] = "Rorqual",
        [Laser] = "Mega Pulse Laser II", [Crystal] = "Multifrequency L", [Plex] = "PLEX", [Hob] = "Hobgoblin II",
    };

    private static string TypeName(long t) => TypeNames.GetValueOrDefault(t, $"Type {t}");

    private static RawAsset Item(long id, long type, long loc, string flag = "Hangar", string locType = "item", long qty = 1) =>
        new(id, type, loc, flag, locType, qty);

    [Fact]
    public void Item_loose_in_station_hangar_has_no_path()
    {
        var items = new[] { Item(1, Plex, Station, "Hangar", "station", 500) };
        var place = AssetLocator.Locate(items, TypeName, new Dictionary<long, string>())[1];
        Assert.Equal(new AssetPlace(Station, OwnershipFlag.Hangar, ""), place);
    }

    [Fact]
    public void Fitted_module_names_the_ship_and_slot()
    {
        var items = new[]
        {
            Item(10, Paladin, Station, "Hangar", "station"),
            Item(11, Laser, 10, "HiSlot3"),
        };
        var places = AssetLocator.Locate(items, TypeName, new Dictionary<long, string> { [10] = "Lasers Go Brr" });
        Assert.Equal(new AssetPlace(Station, OwnershipFlag.Fitted, "Paladin “Lasers Go Brr” › High slot"), places[11]);
        Assert.Equal(new AssetPlace(Station, OwnershipFlag.Hangar, ""), places[10]);
    }

    [Fact]
    public void Two_ships_of_the_same_type_are_told_apart_by_name()
    {
        var items = new[]
        {
            Item(10, Paladin, Station, "Hangar", "station"),
            Item(20, Paladin, Station, "Hangar", "station"),
            Item(11, Crystal, 10, "Cargo", qty: 8),
            Item(21, Crystal, 20, "Cargo", qty: 4),
        };
        var names = new Dictionary<long, string> { [10] = "Alpha", [20] = "Bravo" };
        var places = AssetLocator.Locate(items, TypeName, names);
        Assert.Equal("Paladin “Alpha” › Cargo", places[11].Path);
        Assert.Equal("Paladin “Bravo” › Cargo", places[21].Path);
        Assert.Equal(OwnershipFlag.Container, places[11].Flag);
    }

    [Fact]
    public void Nested_containers_are_described_outermost_first()
    {
        var items = new[]
        {
            Item(1, Rorqual, Station, "Hangar", "station"),
            Item(2, Ssc, 1, "FleetHangar"),
            Item(3, Crystal, 2, "Unlocked", qty: 100),
        };
        var place = AssetLocator.Locate(items, TypeName, new Dictionary<long, string> { [2] = "Ammo" })[3];
        Assert.Equal(new AssetPlace(Station, OwnershipFlag.Container, "Rorqual › Fleet hangar › Small Secure Container “Ammo”"), place);
    }

    [Fact]
    public void Unnamed_holder_falls_back_to_type_name()
    {
        var items = new[] { Item(1, Ssc, Station, "Hangar", "station"), Item(2, Plex, 1, "Locked") };
        var names = new Dictionary<long, string> { [1] = "None" };
        Assert.Equal("Small Secure Container", AssetLocator.Locate(items, TypeName, names)[2].Path);
    }

    [Fact]
    public void Ship_in_space_uses_synthetic_negative_system_id_and_drone_bay_counts_as_fitted()
    {
        var items = new[] { Item(1, Paladin, System, "Hangar", "solar_system"), Item(2, Hob, 1, "DroneBay", qty: 5) };
        var place = AssetLocator.Locate(items, TypeName, new Dictionary<long, string>())[2];
        Assert.Equal(new AssetPlace(-System, OwnershipFlag.Fitted, "Paladin › Drone bay"), place);
    }

    [Fact]
    public void Holder_missing_from_the_list_means_item_is_at_that_location()
    {
        // location_id points at something that is not one of our items (e.g. a structure): treat as the root
        var items = new[] { Item(1, Plex, 1_035_466_617_946, "Hangar", "item", 3) };
        Assert.Equal(new AssetPlace(1_035_466_617_946, OwnershipFlag.Hangar, ""), AssetLocator.Locate(items, TypeName, new Dictionary<long, string>())[1]);
    }

    [Fact]
    public void Cycle_in_location_chain_does_not_hang()
    {
        var items = new[] { Item(1, Ssc, 2), Item(2, Ssc, 1) };
        var places = AssetLocator.Locate(items, TypeName, new Dictionary<long, string>());
        Assert.Equal(2, places.Count);
    }

    [Fact]
    public void Parent_ids_are_only_items_that_hold_other_items()
    {
        var items = new[]
        {
            Item(1, Rorqual, Station, "Hangar", "station"), Item(2, Ssc, 1, "FleetHangar"),
            Item(3, Plex, 2, "Unlocked"), Item(4, Plex, Station, "Hangar", "station"),
        };
        Assert.Equal(new HashSet<long> { 1, 2 }, AssetLocator.ParentIds(items));
    }

    [Theory]
    [InlineData("HiSlot0", "High slot")]
    [InlineData("MedSlot7", "Mid slot")]
    [InlineData("LoSlot2", "Low slot")]
    [InlineData("RigSlot1", "Rig slot")]
    [InlineData("SubSystemSlot3", "Subsystem")]
    [InlineData("Cargo", "Cargo")]
    [InlineData("DroneBay", "Drone bay")]
    [InlineData("FleetHangar", "Fleet hangar")]
    [InlineData("SpecializedOreHold", "Ore hold")]
    [InlineData("FighterTube2", "Fighter tube")]
    [InlineData("Unlocked", "")]
    [InlineData("Locked", "")]
    [InlineData("AutoFit", "")]
    [InlineData("Hangar", "")]
    public void Bay_labels(string flag, string expected) => Assert.Equal(expected, AssetLocator.BayLabel(flag));

    [Theory]
    [InlineData("Paladin", null, "Paladin")]
    [InlineData("Paladin", "", "Paladin")]
    [InlineData("Paladin", "None", "Paladin")]
    [InlineData("Paladin", "paladin", "Paladin")]
    [InlineData("Paladin", " Alpha ", "Paladin “Alpha”")]
    public void Holder_labels(string type, string? name, string expected) => Assert.Equal(expected, AssetLocator.Label(type, name));
}
