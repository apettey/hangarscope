using System.Text.Json;

namespace HangarScope.Services;

public sealed class UniverseCache
{
    public Dictionary<long, TypeEntry> Types { get; set; } = new();
    public Dictionary<long, StationEntry> Stations { get; set; } = new();
    public Dictionary<long, SystemEntry> Systems { get; set; } = new();
    public Dictionary<long, long> Constellations { get; set; } = new();
    public Dictionary<long, string> Regions { get; set; } = new();

    public sealed class TypeEntry { public string Name { get; set; } = ""; public string Group { get; set; } = ""; public string Category { get; set; } = ""; public long GroupId { get; set; } }
    public sealed class StationEntry { public string Name { get; set; } = ""; public long SystemId { get; set; } public bool IsStructure { get; set; } }
    public sealed class SystemEntry { public string Name { get; set; } = ""; public double Sec { get; set; } public long ConstellationId { get; set; } public long RegionId { get; set; } }
}

/// <summary>Resolves and caches type / station / structure / system / region info from ESI.</summary>
public sealed class UniverseService
{
    private readonly EsiClient _esi;
    private readonly JsonStore _store;
    public UniverseCache Cache { get; private set; }
    private readonly Dictionary<long, UniverseCache.TypeEntry> _groupCache = new();

    public UniverseService(EsiClient esi, JsonStore store)
    {
        _esi = esi;
        _store = store;
        Cache = store.Load<UniverseCache>("cache/universe.json");
    }

    public void Persist() => _store.Save("cache/universe.json", Cache);

    public void Reset() { Cache = new UniverseCache(); }

    public async Task ResolveTypes(IEnumerable<long> typeIds, CancellationToken ct)
    {
        var missing = typeIds.Distinct().Where(id => !Cache.Types.ContainsKey(id)).ToList();
        var groups = new Dictionary<long, (string name, long catId)>();
        var tasks = missing.Select(async id =>
        {
            try
            {
                using var doc = await _esi.GetJson($"/universe/types/{id}/", null, ct);
                var name = doc.RootElement.GetProperty("name").GetString() ?? $"Type {id}";
                var groupId = doc.RootElement.GetProperty("group_id").GetInt64();
                return (id, name, groupId, ok: true);
            }
            catch { return (id, $"Type {id}", 0L, ok: false); }
        }).ToList();

        foreach (var t in tasks)
        {
            var (id, name, groupId, ok) = await t;
            Cache.Types[id] = new UniverseCache.TypeEntry { Name = name, GroupId = groupId, Group = "", Category = "" };
        }

        var missingGroups = Cache.Types.Values.Where(t => t.GroupId != 0 && t.Group == "").Select(t => t.GroupId).Distinct().ToList();
        foreach (var gid in missingGroups)
        {
            try
            {
                using var g = await _esi.GetJson($"/universe/groups/{gid}/", null, ct);
                var gname = g.RootElement.GetProperty("name").GetString() ?? "";
                var catId = g.RootElement.GetProperty("category_id").GetInt64();
                string cname;
                using (var c = await _esi.GetJson($"/universe/categories/{catId}/", null, ct))
                    cname = c.RootElement.GetProperty("name").GetString() ?? "";
                foreach (var t in Cache.Types.Values.Where(t => t.GroupId == gid))
                {
                    t.Group = gname;
                    t.Category = NormalizeCategory(cname);
                }
            }
            catch { /* leave unresolved */ }
        }
    }

    /// <summary>Maps ESI category names onto the design's filter categories.</summary>
    private static string NormalizeCategory(string esiCategory) => esiCategory switch
    {
        "Ship" => "Ships",
        "Module" => "Modules",
        "Charge" => "Ammunition",
        "Drone" => "Drones",
        "Fighter" => "Drones",
        "Blueprint" => "Blueprints",
        "Implant" => "Implants",
        "Material" => "Minerals",
        "Asteroid" => "Minerals",
        "Commodity" => "Commodities",
        _ => esiCategory,
    };

    public async Task<UniverseCache.StationEntry?> ResolveLocation(long locationId, string? token, string citadelPolicy, CancellationToken ct)
    {
        if (Cache.Stations.TryGetValue(locationId, out var hit)) return hit;
        try
        {
            if (locationId is >= 60000000 and < 64000000) // NPC station
            {
                using var doc = await _esi.GetJson($"/universe/stations/{locationId}/", null, ct);
                var e = new UniverseCache.StationEntry
                {
                    Name = doc.RootElement.GetProperty("name").GetString() ?? $"Station {locationId}",
                    SystemId = doc.RootElement.GetProperty("system_id").GetInt64(),
                };
                Cache.Stations[locationId] = e;
                return e;
            }
            if (locationId > 1_000_000_000_000) // player structure
            {
                if (citadelPolicy == "off" || token == null) return null;
                using var doc = await _esi.GetJson($"/universe/structures/{locationId}/", token, ct);
                var e = new UniverseCache.StationEntry
                {
                    Name = doc.RootElement.GetProperty("name").GetString() ?? $"Structure {locationId}",
                    SystemId = doc.RootElement.GetProperty("solar_system_id").GetInt64(),
                    IsStructure = true,
                };
                Cache.Stations[locationId] = e;
                return e;
            }
        }
        catch { /* forbidden structure or unknown id */ }
        return null;
    }

    public async Task<UniverseCache.SystemEntry?> ResolveSystem(long systemId, CancellationToken ct)
    {
        if (Cache.Systems.TryGetValue(systemId, out var hit) && hit.RegionId != 0) return hit;
        try
        {
            using var doc = await _esi.GetJson($"/universe/systems/{systemId}/", null, ct);
            var e = new UniverseCache.SystemEntry
            {
                Name = doc.RootElement.GetProperty("name").GetString() ?? $"System {systemId}",
                Sec = doc.RootElement.GetProperty("security_status").GetDouble(),
                ConstellationId = doc.RootElement.GetProperty("constellation_id").GetInt64(),
            };
            if (!Cache.Constellations.TryGetValue(e.ConstellationId, out var regionId))
            {
                using var cdoc = await _esi.GetJson($"/universe/constellations/{e.ConstellationId}/", null, ct);
                regionId = cdoc.RootElement.GetProperty("region_id").GetInt64();
                Cache.Constellations[e.ConstellationId] = regionId;
            }
            e.RegionId = regionId;
            if (!Cache.Regions.ContainsKey(regionId))
            {
                using var rdoc = await _esi.GetJson($"/universe/regions/{regionId}/", null, ct);
                Cache.Regions[regionId] = rdoc.RootElement.GetProperty("name").GetString() ?? $"Region {regionId}";
            }
            Cache.Systems[systemId] = e;
            return e;
        }
        catch { return null; }
    }

    public string RegionName(long systemId) =>
        Cache.Systems.TryGetValue(systemId, out var s) && Cache.Regions.TryGetValue(s.RegionId, out var r) ? r : "Unknown Region";
}
