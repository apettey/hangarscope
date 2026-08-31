namespace HangarScope.Models;

public enum OwnershipFlag { Hangar, Fitted, Container }

public sealed class CharacterInfo
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#4fc3f7";
    public long? SystemId { get; set; }
    public string? SystemName { get; set; }
    public string? RegionName { get; set; }
    public double? SecStatus { get; set; }
    public string? ShipName { get; set; }
    public bool Docked { get; set; }
    public string? StationName { get; set; }
    public List<string> ScopesGranted { get; set; } = new();
    public DateTimeOffset? TokenExpiresAt { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
    public double WalletBalance { get; set; }
    public double WalletDelta30d { get; set; }
}

public sealed class StationInfo
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public long SystemId { get; set; }
    public string SystemName { get; set; } = "";
    public string RegionName { get; set; } = "";
    /// <summary>Jumps from each character's current system, keyed by character id.</summary>
    public Dictionary<long, int> Jumps { get; set; } = new();
}

public sealed class AssetStack
{
    public long TypeId { get; set; }
    public string Name { get; set; } = "";
    public string Group { get; set; } = "";
    public string Category { get; set; } = "";
    public long Qty { get; set; }
    public long CharId { get; set; }
    public long StationId { get; set; }
    public OwnershipFlag Flag { get; set; }
    public double Sell { get; set; }
    public double Buy { get; set; }
    /// <summary>Precomputed lowercase "name group station" blob so search never allocates per keystroke.</summary>
    public string Search { get; set; } = "";
}

public sealed class JournalEntry
{
    public string Id { get; set; } = "";
    public DateTimeOffset Ts { get; set; }
    public long CharId { get; set; }
    public string Desc { get; set; } = "";
    public string RefType { get; set; } = "";
    public double Amount { get; set; }
    public long? StationId { get; set; }
}

public sealed class Snapshot
{
    public bool Demo { get; set; }
    public List<CharacterInfo> Characters { get; set; } = new();
    public Dictionary<long, StationInfo> Stations { get; set; } = new();
    public List<AssetStack> Assets { get; set; } = new();
    public List<JournalEntry> Journal { get; set; } = new();
    public DateTimeOffset? PriceSyncedAt { get; set; }
    public long CacheSizeBytes { get; set; }
}

public sealed class AppSettings
{
    public string ClientId { get; set; } = "";
    public int CallbackPort { get; set; } = 8635;
    public string PriceHub { get; set; } = "jita";
    public string DefaultPriceSide { get; set; } = "sell";
    public int PriceRefreshMin { get; set; } = 30;
    public int AssetSyncMin { get; set; } = 60;
    public int LocationTrackSec { get; set; } = 30;
    public string CitadelPolicy { get; set; } = "docked";
    public bool CredentialsVerified { get; set; }
}

public sealed class AuthRecord
{
    public long CharacterId { get; set; }
    public string CharacterName { get; set; } = "";
    public string RefreshTokenEnc { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public DateTimeOffset AccessTokenExpiresAt { get; set; }
    public List<string> Scopes { get; set; } = new();
    public DateTimeOffset? LastSyncAt { get; set; }
}

public static class EsiScopes
{
    public static readonly string[] Required =
    {
        "esi-assets.read_assets.v1",
        "esi-location.read_location.v1",
        "esi-location.read_ship_type.v1",
        "esi-universe.read_structures.v1",
        "esi-wallet.read_character_wallet.v1",
    };
}

public static class CharacterPalette
{
    public static readonly string[] Colors =
        { "#4fc3f7", "#ffb300", "#8bd450", "#c0533f", "#b48ead", "#7c9a5e", "#8fd9fa", "#d8a657" };
}

public sealed class PriceHubDef
{
    public required string Key { get; init; }
    public required long RegionId { get; init; }
    public required long StationId { get; init; }
    public required string Label { get; init; }

    public static readonly PriceHubDef[] All =
    {
        new() { Key = "jita", RegionId = 10000002, StationId = 60003760, Label = "JITA 4-4" },
        new() { Key = "amarr", RegionId = 10000043, StationId = 60008494, Label = "AMARR" },
        new() { Key = "dodixie", RegionId = 10000032, StationId = 60011866, Label = "DODIXIE" },
        new() { Key = "rens", RegionId = 10000030, StationId = 60004588, Label = "RENS" },
    };

    public static PriceHubDef ByKey(string key) => All.FirstOrDefault(h => h.Key == key) ?? All[0];
}
