using Avalonia.Data.Converters;
using Avalonia.Media;

namespace HangarScope.Views;

/// <summary>Small bool → brush/weight converters for state-dependent styling.</summary>
public static class Conv
{
    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    public static readonly IValueConverter TabBorder =
        new FuncValueConverter<bool, IBrush>(active => active ? Brush("#4fc3f7") : Brushes.Transparent);

    public static readonly IValueConverter TabText =
        new FuncValueConverter<bool, IBrush>(active => active ? Brush("#eef4f8") : Brush("#6b7887"));

    public static readonly IValueConverter ToggleBg =
        new FuncValueConverter<bool, IBrush>(on => on ? Brush("#4fc3f7") : Brushes.Transparent);

    public static readonly IValueConverter ToggleFg =
        new FuncValueConverter<bool, IBrush>(on => on ? Brush("#0b0d10") : Brush("#6b7887"));

    public static readonly IValueConverter ToggleWeight =
        new FuncValueConverter<bool, FontWeight>(on => on ? FontWeight.SemiBold : FontWeight.Normal);

    public static readonly IValueConverter ChipBorder =
        new FuncValueConverter<bool, IBrush>(sel => sel ? Brush("#3a4a5c") : Brush("#1e2630"));

    public static readonly IValueConverter ChipBg =
        new FuncValueConverter<bool, IBrush>(sel => sel ? Brush("#131a22") : Brushes.Transparent);

    public static readonly IValueConverter ChipText =
        new FuncValueConverter<bool, IBrush>(sel => sel ? Brush("#e4ebf1") : Brush("#8fa0b0"));

    public static readonly IValueConverter StationBorder =
        new FuncValueConverter<bool, IBrush>(sel => sel ? Brush("#4fc3f7") : Brush("#1e2630"));

    public static readonly IValueConverter StationText =
        new FuncValueConverter<bool, IBrush>(sel => sel ? Brush("#4fc3f7") : Brush("#8fa0b0"));

    public static readonly IValueConverter FlagBorder =
        new FuncValueConverter<bool, IBrush>(fitted => fitted ? Brush("#4a3a10") : Brush("#26313d"));

    public static readonly IValueConverter FlagText =
        new FuncValueConverter<bool, IBrush>(fitted => fitted ? Brush("#ffb300") : Brush("#8fa0b0"));

    public static readonly IValueConverter GreenDot =
        new FuncValueConverter<bool, IBrush>(on => on ? Brush("#8bd450") : Brush("#6b7887"));
}
