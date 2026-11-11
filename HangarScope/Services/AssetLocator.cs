using System.Text;
using HangarScope.Models;

namespace HangarScope.Services;

/// <summary>One row of ESI /characters/{id}/assets/, reduced to what locating it needs.</summary>
public sealed record RawAsset(long ItemId, long TypeId, long LocationId, string LocationFlag, string LocationType, long Qty);

/// <summary>Where an item sits: the station (or synthetic in-space id), the ownership flag, and the container/ship path inside it.</summary>
public sealed record AssetPlace(long StationId, OwnershipFlag Flag, string Path);

/// <summary>
/// Walks the container/ship chain of every asset up to its station, structure or solar system, and describes the
/// chain in words ("Paladin “Lasers Go Brr” › Cargo") so the ASSETS table can say exactly which container or ship
/// holds an item. Pure: no ESI calls, so it is covered by HangarScope.Tests.
/// </summary>
public static class AssetLocator
{
    public const string Separator = " › ";
    private const int MaxDepth = 16;

    /// <summary>Location flags that just mean "inside this container" and add nothing to the path.</summary>
    private static readonly HashSet<string> SilentFlags = new(StringComparer.Ordinal)
    {
        "Unlocked", "Locked", "AutoFit", "Hangar", "HangarAll", "",
    };

    /// <summary>Module slots, drone and fighter bays: the flags that make an item "fitted" rather than carried.</summary>
    public static bool IsFittingFlag(string f) =>
        f.StartsWith("HiSlot") || f.StartsWith("MedSlot") || f.StartsWith("LoSlot") ||
        f.StartsWith("RigSlot") || f.StartsWith("SubSystemSlot") || f is "DroneBay" or "FighterBay";

    /// <summary>Item ids that hold other items: the containers and ships whose player-given names are worth fetching.</summary>
    public static HashSet<long> ParentIds(IReadOnlyList<RawAsset> items)
    {
        var ids = new HashSet<long>(items.Count);
        foreach (var i in items) ids.Add(i.ItemId);
        var parents = new HashSet<long>();
        foreach (var i in items)
            if (ids.Contains(i.LocationId)) parents.Add(i.LocationId);
        return parents;
    }

    /// <summary>
    /// Locates every item. <paramref name="typeName"/> names a type; <paramref name="itemNames"/> holds player-given
    /// names of containers and ships (from /assets/names/), missing for unnamed ones.
    /// </summary>
    public static Dictionary<long, AssetPlace> Locate(
        IReadOnlyList<RawAsset> items, Func<long, string> typeName, IReadOnlyDictionary<long, string> itemNames)
    {
        var byId = new Dictionary<long, RawAsset>(items.Count);
        foreach (var i in items) byId[i.ItemId] = i;
        var labels = new Dictionary<long, string>();
        var result = new Dictionary<long, AssetPlace>(items.Count);
        var chain = new List<RawAsset>();

        foreach (var item in items)
        {
            // walk up: item → its container/ship → … → the outermost item, which sits at a station/structure/system
            chain.Clear();
            var cur = item;
            while (chain.Count < MaxDepth && byId.TryGetValue(cur.LocationId, out var parent) && parent.ItemId != item.ItemId)
            {
                chain.Add(cur);
                cur = parent;
            }

            var flag = chain.Count == 0 ? OwnershipFlag.Hangar
                : IsFittingFlag(item.LocationFlag) ? OwnershipFlag.Fitted : OwnershipFlag.Container;
            var stationId = cur.LocationType == "solar_system" ? -cur.LocationId : cur.LocationId; // synthetic "in space" station keyed by system

            // describe from the outermost holder inwards: each holder's label, then the bay the next step sits in
            var sb = new StringBuilder();
            for (var k = chain.Count - 1; k >= 0; k--)
            {
                var holder = byId[chain[k].LocationId];
                if (sb.Length > 0) sb.Append(Separator);
                if (!labels.TryGetValue(holder.ItemId, out var label))
                    labels[holder.ItemId] = label = Label(typeName(holder.TypeId), itemNames.GetValueOrDefault(holder.ItemId));
                sb.Append(label);
                var bay = BayLabel(chain[k].LocationFlag);
                if (bay.Length > 0) sb.Append(Separator).Append(bay);
            }
            result[item.ItemId] = new AssetPlace(stationId, flag, sb.ToString());
        }
        return result;
    }

    /// <summary>"Paladin “Lasers Go Brr”" for a named holder, the bare type name otherwise.</summary>
    public static string Label(string typeName, string? name)
    {
        var n = name?.Trim();
        return string.IsNullOrEmpty(n) || n == "None" || string.Equals(n, typeName, StringComparison.OrdinalIgnoreCase)
            ? typeName
            : $"{typeName} “{n}”";
    }

    /// <summary>
    /// Human label for the bay/slot an item sits in: HiSlot3 → "High slot", DroneBay → "Drone bay",
    /// SpecializedOreHold → "Ore hold". Plain container flags (Unlocked, Locked…) give "".
    /// </summary>
    public static string BayLabel(string flag)
    {
        if (SilentFlags.Contains(flag)) return "";
        if (flag.StartsWith("HiSlot")) return "High slot";
        if (flag.StartsWith("MedSlot")) return "Mid slot";
        if (flag.StartsWith("LoSlot")) return "Low slot";
        if (flag.StartsWith("RigSlot")) return "Rig slot";
        if (flag.StartsWith("SubSystemSlot")) return "Subsystem";
        var f = flag.StartsWith("Specialized") && flag.Length > "Specialized".Length ? flag["Specialized".Length..] : flag;
        f = f.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        var sb = new StringBuilder(f.Length + 4);
        for (var i = 0; i < f.Length; i++)
        {
            var c = f[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(f[i - 1])) sb.Append(' ').Append(char.ToLowerInvariant(c));
            else sb.Append(i == 0 ? char.ToUpperInvariant(c) : c);
        }
        return sb.ToString();
    }
}
