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

        // ---- REFIT demo: three Paladins at 1DQ1-A on three pilots, one spare in Amarr, a few spare modules
        var types = RefitTypes();
        var ships = new List<ShipInstance>
        {
            Ship(9101, "Lasers Go Brr", K, 5,
                (Mpl, "HiSlot", 4), (Bast, "HiSlot", 1), (Tract, "HiSlot", 1), (Salv, "HiSlot", 1),
                (Mjd, "MedSlot", 1), (Tc, "MedSlot", 2), (Hcb, "MedSlot", 1),
                (Lar, "LoSlot", 2), (Hs, "LoSlot", 3), (Te, "LoSlot", 1), (Eanm, "LoSlot", 1),
                (Elc, "RigSlot", 1), (Ema, "RigSlot", 1), (Hob, "DroneBay", 5), (Imf, "Cargo", 8), (Hob, "Cargo", 6)),
            Ship(9102, "Crater Anchor", M, 5,
                (Mpl, "HiSlot", 4), (Bast, "HiSlot", 1), (Dla, "HiSlot", 1), (Tract, "HiSlot", 1),
                (Mjd, "MedSlot", 1), (Tc, "MedSlot", 1), (Sb, "MedSlot", 1), (Hcb, "MedSlot", 1),
                (Lar, "LoSlot", 1), (Hs, "LoSlot", 3), (Te, "LoSlot", 2), (Dda, "LoSlot", 1),
                (Elc, "RigSlot", 2), (Ham, "DroneBay", 5)),
            Ship(9103, "Sergeant's Old Fit", R, 5,
                (Mpl, "HiSlot", 4), (Bast, "HiSlot", 1), (Salv, "HiSlot", 2),
                (Mjd, "MedSlot", 1), (Tc, "MedSlot", 1), (Hcb, "MedSlot", 2),
                (Lar, "LoSlot", 2), (Hs, "LoSlot", 2), (Eanm, "LoSlot", 2), (Dda, "LoSlot", 1),
                (Ecr, "RigSlot", 1), (Ema, "RigSlot", 1), (Hob, "DroneBay", 3)),
            Ship(9104, "Hisec Spare", M, 3,
                (Mpl, "HiSlot", 4), (Bast, "HiSlot", 1), (Mjd, "MedSlot", 1), (Lar, "LoSlot", 2), (Hs, "LoSlot", 4)),
        };
        // loaded crystals sit under the laser's HiSlot flag in real ESI data: charges, never modules
        foreach (var sh in ships)
            foreach (var slot in sh.Items.Where(i => i.TypeId == Mpl).Select(i => i.Flag).ToList())
                sh.Items.Add(new FitItem { TypeId = Imf, Flag = slot, Qty = 1 });

        void T(long typeId, long qty, long ch, long st, OwnershipFlag flag)
        {
            var t = types[typeId];
            assets.Add(new AssetStack
            {
                TypeId = typeId, Name = t.Name, Group = t.Group, Category = t.Category, Qty = qty, CharId = ch, StationId = st,
                Flag = flag, Sell = t.Sell, Buy = t.Buy, Search = $"{t.Name} {t.Group} {stations[st].Name}".ToLowerInvariant(),
            });
        }
        foreach (var sh in ships)
        {
            T(Paladin, 1, sh.CharId, sh.StationId, OwnershipFlag.Hangar);
            foreach (var g in sh.Items.GroupBy(i => (i.TypeId, fitted: FitMath.IsFittedFlag(i.Flag))))
                T(g.Key.TypeId, g.Sum(i => i.Qty), sh.CharId, sh.StationId, g.Key.fitted ? OwnershipFlag.Fitted : OwnershipFlag.Container);
        }
        T(Hs, 4, R, 5, OwnershipFlag.Container);
        T(Tc, 1, M, 5, OwnershipFlag.Hangar);
        T(Elc, 1, K, 1, OwnershipFlag.Hangar);
        T(Te, 2, M, 3, OwnershipFlag.Hangar);

        var fittings = new List<SavedFitting>
        {
            new() { FittingId = 1, CharId = K, Name = "Crater Paladin · Doctrine v2", ShipTypeId = Paladin, Items = Doctrine().ToList() },
            new() { FittingId = 2, CharId = M, Name = "Crater Paladin · Doctrine v2", ShipTypeId = Paladin, Items = Doctrine().ToList() },
            new() { FittingId = 3, CharId = K, Name = "Paladin · Blitz C5", ShipTypeId = Paladin, Items = Blitz().ToList() },
            new() { FittingId = 4, CharId = R, Name = "Ishtar · Sentry anoms", ShipTypeId = Ishtar, Items = Sentry().ToList() },
            new() { FittingId = 5, CharId = K, Name = "Ishtar · Sentry anoms", ShipTypeId = Ishtar, Items = Sentry().ToList() },
            new() { FittingId = 6, CharId = M, Name = "Ishtar · Sentry", ShipTypeId = Ishtar, Items = Sentry().ToList() },
        };

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
            Ships = ships,
            Fittings = fittings,
            Types = types,
            TypeNames = types.ToDictionary(kv => kv.Key, kv => kv.Value.Name),
            PriceSyncedAt = DateTimeOffset.UtcNow.AddMinutes(-4),
            CacheSizeBytes = (long)(18.4 * 1024 * 1024),
        };
    }

    // demo-only type ids for the REFIT dataset
    private const long Paladin = 990001, Ishtar = 990002, Mpl = 990010, Bast = 990011, Tract = 990012, Salv = 990013, Dla = 990014,
        Mjd = 990020, Tc = 990021, Hcb = 990022, Sb = 990023, Lar = 990030, Hs = 990031, Te = 990032, Eanm = 990033, Dda = 990034,
        Elc = 990040, Ema = 990041, Ecr = 990042, Hob = 990050, Ham = 990051, Imf = 990060;

    private static Dictionary<long, TypeMeta> RefitTypes()
    {
        static TypeMeta Ty(string name, string group, string cat, double sell, SlotLayout? slots = null) =>
            new() { Name = name, Group = group, Category = cat, Sell = sell, Buy = sell * 0.92, Slots = slots };
        return new Dictionary<long, TypeMeta>
        {
            [Paladin] = Ty("Paladin", "Marauder", "Ships", 1.62e9, new SlotLayout { High = 8, Med = 4, Low = 7, Rig = 2 }),
            [Ishtar] = Ty("Ishtar", "Heavy Assault Cruiser", "Ships", 320e6, new SlotLayout { High = 5, Med = 4, Low = 5, Rig = 2 }),
            [Mpl] = Ty("Mega Pulse Laser II", "Energy Weapon", "Modules", 3.1e6),
            [Bast] = Ty("Bastion Module I", "Siege Module", "Modules", 61e6),
            [Tract] = Ty("Small Tractor Beam II", "Tractor Beam", "Modules", 1.9e6),
            [Salv] = Ty("Salvager II", "Salvager", "Modules", 0.9e6),
            [Dla] = Ty("Drone Link Augmentor II", "Drone Control Range Module", "Modules", 4.4e6),
            [Mjd] = Ty("Large Micro Jump Drive", "Micro Jump Drive", "Modules", 27.5e6),
            [Tc] = Ty("Tracking Computer II", "Tracking Computer", "Modules", 2.6e6),
            [Hcb] = Ty("Heavy Capacitor Booster II", "Capacitor Booster", "Modules", 6.3e6),
            [Sb] = Ty("Sensor Booster II", "Sensor Booster", "Modules", 1.2e6),
            [Lar] = Ty("Large Armor Repairer II", "Armor Repair Unit", "Modules", 3.4e6),
            [Hs] = Ty("Heat Sink II", "Heat Sink", "Modules", 1.1e6),
            [Te] = Ty("Tracking Enhancer II", "Tracking Enhancer", "Modules", 1.0e6),
            [Eanm] = Ty("Energized Adaptive Nano Membrane II", "Armor Plating Energized", "Modules", 2.2e6),
            [Dda] = Ty("Drone Damage Amplifier II", "Drone Damage Modules", "Modules", 1.6e6),
            [Elc] = Ty("Large Energy Locus Coordinator II", "Rig Energy Weapon", "Modules", 28e6),
            [Ema] = Ty("Large Energy Metastasis Adjuster I", "Rig Energy Weapon", "Modules", 2.4e6),
            [Ecr] = Ty("Large Energy Collision Accelerator I", "Rig Energy Weapon", "Modules", 1.8e6),
            [Hob] = Ty("Hobgoblin II", "Combat Drone", "Drones", 0.55e6),
            [Ham] = Ty("Hammerhead II", "Combat Drone", "Drones", 1.3e6),
            [Imf] = Ty("Imperial Navy Multifrequency L", "Advanced Frequency Crystal", "Ammunition", 2.1e6),
        };
    }

    private static FitItem[] Doctrine() => Items(
        (Mpl, "HiSlot", 4), (Bast, "HiSlot", 1), (Tract, "HiSlot", 1), (Salv, "HiSlot", 1), (Dla, "HiSlot", 1),
        (Mjd, "MedSlot", 1), (Tc, "MedSlot", 2), (Hcb, "MedSlot", 1),
        (Lar, "LoSlot", 2), (Hs, "LoSlot", 3), (Te, "LoSlot", 1), (Eanm, "LoSlot", 1),
        (Elc, "RigSlot", 2), (Hob, "DroneBay", 5), (Imf, "Cargo", 8));

    private static FitItem[] Blitz() => Items(
        (Mpl, "HiSlot", 4), (Bast, "HiSlot", 1), (Dla, "HiSlot", 1),
        (Mjd, "MedSlot", 1), (Tc, "MedSlot", 2), (Sb, "MedSlot", 1),
        (Hs, "LoSlot", 4), (Te, "LoSlot", 2), (Dda, "LoSlot", 1),
        (Elc, "RigSlot", 2), (Ham, "DroneBay", 5));

    private static FitItem[] Sentry() => Items(
        (Dla, "HiSlot", 2), (Tc, "MedSlot", 2), (Dda, "LoSlot", 3), (Lar, "LoSlot", 1), (Hob, "DroneBay", 5));

    private static FitItem[] Items(params (long type, string slot, long qty)[] items)
    {
        // modules get one numbered slot each (HiSlot0, HiSlot1…); bays and cargo keep one stack
        var next = new Dictionary<string, int>();
        var list = new List<FitItem>();
        foreach (var (type, slot, qty) in items)
        {
            if (slot is "DroneBay" or "Cargo") { list.Add(new FitItem { TypeId = type, Flag = slot, Qty = qty }); continue; }
            for (var i = 0; i < qty; i++)
            {
                var n = next.GetValueOrDefault(slot);
                next[slot] = n + 1;
                list.Add(new FitItem { TypeId = type, Flag = slot + n, Qty = 1 });
            }
        }
        return list.ToArray();
    }

    private static ShipInstance Ship(long itemId, string name, long ch, long st, params (long type, string slot, long qty)[] items) => new()
    {
        ItemId = itemId, TypeId = Paladin, Name = name, CharId = ch, StationId = st, Items = Items(items).ToList(),
    };

    /// <summary>Initial REFIT state in demo mode: the three 1DQ1-A Paladins onto the doctrine fit, plus one new hull.</summary>
    public static SavedRefitPlan RefitDefaults() => new()
    {
        ShipFilter = "Paladin",
        ShipItemIds = new List<long> { 9101, 9102, 9103 },
        Target = new RefitTarget
        {
            ShipTypeId = Paladin, Name = "Crater Paladin · Doctrine v2",
            Lines = RefitPlanner.ToLines(Doctrine(), RefitTypes()),
        },
        NewHulls = 1,
        NewHullCharId = 1,
    };

    private static StationInfo St(long id, string name, string region, params (long ch, int j)[] jumps) => new()
    {
        Id = id, Name = name, RegionName = region, SystemName = name.Split(' ')[0],
        Jumps = jumps.ToDictionary(x => x.ch, x => x.j),
    };
}
