using System.Windows.Input;
using Avalonia.Media;

namespace HangarScope.ViewModels;

/// <summary>Source / outcome tag chip: SWAP, CONTRACT, PICKUP, BUY, DESTROYED, LEFT OVER, HULL.</summary>
public sealed record TagVm(string Text, IBrush Fg, IBrush Border);

public sealed record PilotDotVm(string Name, IBrush Color, bool Has)
{
    public IBrush Fill => Has ? Color : Brushes.Transparent;
}

public sealed record RefitShipVm(string Title, string Hull, string PilotName, IBrush PilotColor, string WhereText,
    string ItemsText, bool Selected, ICommand Toggle);

public sealed record SummaryCardVm(string Label, string Value, string Sub);

public sealed record MatrixColVm(string Key, IBrush Color, string Tip);

public sealed record MatrixCellVm(string Text, IBrush Brush);

public sealed record MatrixRowVm(
    bool IsSlot, string Name, string SlotStatus, IBrush SlotBrush,
    List<MatrixCellVm> Cells, string TargetText, ICommand? Minus, ICommand? Plus,
    List<TagVm> Tags, string CostText)
{
    public bool IsItem => !IsSlot;
    public bool HasTags => Tags.Count > 0;
}

public sealed record OrderLineVm(string QtyText, string Name, List<TagVm> Tags, string FromText)
{
    public bool HasFrom => FromText.Length > 0;
}

public sealed record WorkOrderVm(string Title, string PilotName, IBrush PilotColor,
    List<OrderLineVm> Strip, string StripEmpty, List<OrderLineVm> Fit, string FitEmpty)
{
    public bool HasStrip => Strip.Count > 0;
    public bool HasFit => Fit.Count > 0;
}

public sealed record ListRowVm(string Name, string Sub, string Sub2, IBrush Sub2Color, string QtyText, string RightText,
    IBrush RightColor, List<TagVm> Tags)
{
    public bool HasSub2 => Sub2.Length > 0;
    public bool HasRight => RightText.Length > 0;
}

public sealed record SavedPlanVm(string Name, string SubText, ICommand Load, ICommand Delete);

/// <summary>One fit in the FITTINGS library: identical fits on several pilots (matched by content hash) are one row.</summary>
public sealed class FitLibVm : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public required string Name { get; init; }
    public required string HullName { get; init; }
    public required string ModulesText { get; init; }
    public required List<PilotDotVm> Pilots { get; init; }
    public required string SavedOnText { get; init; }
    public required string MissingText { get; init; }
    public required string OutdatedText { get; init; }
    public required ICommand Use { get; init; }
    public required ICommand SyncTo { get; init; }
    public required ICommand AskDelete { get; init; }
    public bool HasMissing => MissingText.Length > 0;
    public bool HasOutdated => OutdatedText.Length > 0;
}

/// <summary>One character in the SYNC TO picker: checkbox, color dot, name and its state for the fit.</summary>
public sealed record FitSyncPilotVm(string Name, IBrush Color, string StateText, IBrush StateBrush, string Hint,
    bool CanSelect, bool Selected, ICommand Toggle)
{
    public bool HasHint => Hint.Length > 0;
}

/// <summary>Outcome of a confirmed sync on one character.</summary>
public sealed record FitSyncResultVm(string Name, IBrush Color, string Text, IBrush Brush);

/// <summary>A labelled command for menu-style lists (e.g. COPY SHIP ▾).</summary>
public sealed record ActionVm(string Label, string Sub, ICommand Go);
