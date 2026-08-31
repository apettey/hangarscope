using HangarScope.Models;

namespace HangarScope.Services;

/// <summary>The prototype's mock dataset, shown until real characters are linked via SSO.</summary>
public static class DemoData
{
    public static Snapshot Build()
    {
        const long K = 1, M = 2, R = 3;

        var chars = new List<CharacterInfo>
        {
            new() { Id = K, Name = "Keldan Vex", Color = "#4fc3f7", SystemName = "Jita", RegionName = "The Forge", SecStatus = 0.9, ShipName = "Buzzard", Docked = true, StationName = "JITA 4-4", WalletBalance = 4.82e9, WalletDelta30d = 612e6, ScopesGranted = EsiScopes.Required.ToList(), TokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(18), LastSyncAt = DateTimeOffset.UtcNow.AddMinutes(-2) },
            new() { Id = M, Name = "Mira Osteon", Color = "#ffb300", SystemName = "Amarr", RegionName = "Domain", SecStatus = 1.0, ShipName = "Occator", Docked = true, StationName = "EMPEROR FAMILY ACADEMY", WalletBalance = 1.37e9, WalletDelta30d = -184e6, ScopesGranted = EsiScopes.Required.ToList(), TokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(12), LastSyncAt = DateTimeOffset.UtcNow.AddMinutes(-2) },
            new() { Id = R, Name = "Rho Kestrel", Color = "#8bd450", SystemName = "1DQ1-A", RegionName = "Delve", SecStatus = -0.4, ShipName = "Ishtar", Docked = false, WalletBalance = 9.14e9, WalletDelta30d = 1.9e9, ScopesGranted = EsiScopes.Required.ToList(), TokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(4), LastSyncAt = DateTimeOffset.UtcNow.AddMinutes(-41) },
        };

        var stations = new Dictionary<long, StationInfo>
        {
            [1] = St(1, "Jita 4-4 · Caldari Navy Assembly Plant", "The Forge", (K, 0), (M, 9), (R, 38)),
            [2] = St(2, "Perimeter · Tranquility Trading Tower", "The Forge", (K, 1), (M, 10), (R, 39)),
            [3] = St(3, "Amarr VIII · Emperor Family Academy", "Domain", (K, 9), (M, 0), (R, 31)),
            [4] = St(4, "Dodixie IX-20 · Federation Navy", "Sinq Laison", (K, 14), (M, 8), (R, 35)),
            [5] = St(5, "1DQ1-A · 1-st Imperial Palace", "Delve", (K, 38), (M, 31), (R, 0)),
            [6] = St(6, "Rens VI-8 · Brutor Tribe Treasury", "Heimatar", (K, 16), (M, 10), (R, 33)),
        };

        var assets = new List<AssetStack>();
        void A(string name, string group, string cat, long qty, long ch, long st, OwnershipFlag flag, double sell, double buy) =>
            assets.Add(new AssetStack
            {
                TypeId = assets.Count + 1, Name = name, Group = group, Category = cat, Qty = qty, CharId = ch,
                StationId = st, Flag = flag, Sell = sell, Buy = buy,
                Search = $"{name} {group} {stations[st].Name}".ToLowerInvariant(),
            });

        A("Raven Navy Issue", "Battleship", "Ships", 1, K, 1, OwnershipFlag.Hangar, 620e6, 585e6);
        A("Rorqual", "Capital Industrial", "Ships", 1, R, 5, OwnershipFlag.Hangar, 2.6e9, 2.35e9);
        A("Large Skill Injector", "Consumable", "Commodities", 14, K, 2, OwnershipFlag.Hangar, 812e6, 790e6);
        A("Nightmare", "Battleship", "Ships", 1, M, 3, OwnershipFlag.Hangar, 480e6, 440e6);
        A("Ishtar", "Heavy Assault Cruiser", "Ships", 3, R, 5, OwnershipFlag.Hangar, 320e6, 295e6);
        A("PLEX", "Account Item", "Commodities", 500, M, 3, OwnershipFlag.Hangar, 4.1e6, 3.95e6);
        A("Amulet Alpha", "Cybernetics", "Implants", 1, M, 3, OwnershipFlag.Hangar, 175e6, 160e6);
        A("Sisters Expanded Probe Launcher", "Scan Probe Launcher", "Modules", 3, K, 2, OwnershipFlag.Hangar, 95e6, 88e6);
        A("Sabre", "Interdictor", "Ships", 2, K, 1, OwnershipFlag.Hangar, 68e6, 61e6);
        A("Tritanium", "Mineral", "Minerals", 12400000, K, 1, OwnershipFlag.Hangar, 5.1, 4.8);
        A("Gecko", "Combat Drone", "Drones", 40, R, 5, OwnershipFlag.Hangar, 8.5e6, 7.6e6);
        A("Federation Navy Comet", "Frigate", "Ships", 5, K, 4, OwnershipFlag.Hangar, 22e6, 19e6);
        A("Imperial Navy Multispectrum Hardener", "Armor Hardener", "Modules", 12, M, 3, OwnershipFlag.Hangar, 22e6, 19e6);
        A("Oxygen Isotopes", "Ice Product", "Minerals", 800000, M, 6, OwnershipFlag.Hangar, 590, 540);
        A("Compressed Spodumain", "Ore", "Minerals", 42000, R, 5, OwnershipFlag.Hangar, 21000, 19000);
        A("Nanite Repair Paste", "Nanite", "Commodities", 5000, R, 5, OwnershipFlag.Hangar, 28000, 24000);
        A("Ishtar Blueprint (Copy)", "Blueprint", "Blueprints", 6, R, 5, OwnershipFlag.Container, 12e6, 8e6);
        A("Ballistic Control System II", "Ballistic Control", "Modules", 24, K, 1, OwnershipFlag.Container, 1.2e6, 0.9e6);
        A("Scourge Fury Heavy Missile", "Missile", "Ammunition", 250000, R, 5, OwnershipFlag.Container, 210, 180);
        A("425mm AutoCannon II", "Projectile Turret", "Modules", 8, R, 5, OwnershipFlag.Fitted, 3.2e6, 2.7e6);
        A("Damage Control II", "Damage Control", "Modules", 60, K, 4, OwnershipFlag.Hangar, 0.6e6, 0.45e6);
        A("Warrior II", "Combat Drone", "Drones", 300, M, 6, OwnershipFlag.Hangar, 0.3e6, 0.25e6);
        A("Pyerite", "Mineral", "Minerals", 3100000, M, 3, OwnershipFlag.Hangar, 12, 10.5);

        var year = DateTimeOffset.UtcNow.Year;
        var journal = new List<JournalEntry>();
        void J(string mmddhhmm, long ch, string desc, string refType, double amount, long? st)
        {
            var mo = int.Parse(mmddhhmm[..2]); var da = int.Parse(mmddhhmm[3..5]);
            var hh = int.Parse(mmddhhmm[6..8]); var mi = int.Parse(mmddhhmm[9..11]);
            journal.Add(new JournalEntry { Id = "demo-" + journal.Count, Ts = new DateTimeOffset(year, mo, da, hh, mi, 0, TimeSpan.Zero), CharId = ch, Desc = desc, RefType = refType, Amount = amount, StationId = st });
        }
        J("08-30 11:42", R, "Bounty prizes (Delve ratting)", "bounty_prizes", 38.4e6, null);
        J("08-30 10:15", K, "Sold 4 × Large Skill Injector", "market_transaction", 3.21e9, 2);
        J("08-30 10:15", K, "Broker fee", "brokers_fee", -48.2e6, 1);
        J("08-29 22:03", M, "Contract deposit — courier to Rens", "contract_deposit", -12e6, 3);
        J("08-29 19:47", R, "Bought Gecko × 15", "market_transaction", -127.5e6, 5);
        J("08-29 14:20", M, "PLEX sale escrow released", "market_escrow", 2.05e9, 3);
        J("08-28 23:58", K, "Sales tax", "transaction_tax", -96.3e6, 1);
        J("08-28 16:31", R, "Insurance payout — Ishtar", "insurance", 41.7e6, null);
        J("08-28 09:12", M, "Transfer to Rho Kestrel", "player_donation", -500e6, null);
        J("08-27 20:05", K, "Bought Sabre × 2", "market_transaction", -136e6, 1);
        J("08-27 12:40", R, "Sold Compressed Spodumain", "market_transaction", 612e6, 5);
        J("08-26 18:22", M, "Bought Oxygen Isotopes", "market_transaction", -472e6, 6);

        return new Snapshot
        {
            Demo = true,
            Characters = chars,
            Stations = stations,
            Assets = assets,
            Journal = journal,
            PriceSyncedAt = DateTimeOffset.UtcNow.AddMinutes(-4),
            CacheSizeBytes = (long)(18.4 * 1024 * 1024),
        };
    }

    private static StationInfo St(long id, string name, string region, params (long ch, int j)[] jumps) => new()
    {
        Id = id, Name = name, RegionName = region, SystemName = name.Split(' ')[0],
        Jumps = jumps.ToDictionary(x => x.ch, x => x.j),
    };
}
