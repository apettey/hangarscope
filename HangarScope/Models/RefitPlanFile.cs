using HangarScope.Services;

namespace HangarScope.Models;

/// <summary>Contents of %APPDATA%\HangarScope\refits.json: named REFIT plans plus the screen's last working state.</summary>
public sealed class RefitFile
{
    public SavedRefitPlan? Current { get; set; }
    public List<SavedRefitPlan> Plans { get; set; } = new();
}

public sealed class SavedRefitPlan
{
    public string Name { get; set; } = "";
    public DateTimeOffset SavedAt { get; set; }
    public string ShipFilter { get; set; } = "";
    /// <summary>ESI item ids of the selected hulls.</summary>
    public List<long> ShipItemIds { get; set; } = new();
    public RefitTarget Target { get; set; } = new();
    public bool UseSwaps { get; set; } = true;
    public bool UseStock { get; set; } = true;
    public bool AllowContracts { get; set; } = true;
    public bool IncludeDrones { get; set; } = true;
    public bool IncludeCharges { get; set; }
    public bool IncludeCargo { get; set; }
    public int NewHulls { get; set; }
    public long NewHullCharId { get; set; }
}
