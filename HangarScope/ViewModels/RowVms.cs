using System.Windows.Input;
using Avalonia.Media;

namespace HangarScope.ViewModels;

public sealed record TabVm(string Id, string Label, bool Active, ICommand Go);

public sealed record ChipVm(string Name, string ValueText, IBrush Color, bool Selected, ICommand Go)
{
    public IBrush DotFill => Selected ? Color : Brushes.Transparent;
}

public sealed record TreeStationVm(long Id, string Name, string CountText, bool Selected, ICommand Go);

public sealed record TreeRegionVm(string Name, string ValueText, List<TreeStationVm> Stations);

public sealed record AssetRowVm(
    string Name, string FlagLabel, bool FlagVisible, bool FlagFitted,
    string QtyText, string Group, string OwnerName, IBrush OwnerColor,
    string Location, string JumpsText, IBrush JumpsBrush, string UnitText, string ValueText);

public sealed record CharCardVm(string Name, IBrush Color, string ValueText, string SubText);

public sealed record BarVm(string Name, string ValueText, IBrush ValueBrush, double Fraction, IBrush Fill);

public sealed record TopRowVm(string Rank, string Name, string OwnerName, IBrush OwnerColor, string Location, string ValueText);

public sealed record WalletCardVm(string Name, IBrush Color, string BalanceText, string DeltaText, IBrush DeltaBrush);

public sealed record StationFlowVm(string Name, string InText, string OutText, string NetText, IBrush NetBrush);

public sealed record JournalRowVm(string DateText, string OwnerName, IBrush OwnerColor, string Desc, string Station, string TypeText, string AmountText, IBrush AmountBrush);

public sealed record NearestVm(string JumpsText, IBrush JumpsBrush, string Name, string ValueText);

public sealed record LocCardVm(
    string Name, string Initials, IBrush Color, string Status,
    string SystemName, string SecText, IBrush SecBrush,
    string RegionName, string ShipName, string ValueText, List<NearestVm> Nearest);

public sealed record AuthRowVm(string Name, IBrush Color, string ScopesText, string ExpiresText, string SyncText, ICommand Sync, ICommand Unlink);

public sealed record OptionVm(string Label, object? Value)
{
    public override string ToString() => Label;
}
