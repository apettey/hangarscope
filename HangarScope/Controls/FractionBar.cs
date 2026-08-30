using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HangarScope.Controls;

/// <summary>A 6px horizontal value bar: dark track with a proportional fill, per the design's region/flow bars.</summary>
public sealed class FractionBar : Control
{
    public static readonly StyledProperty<double> FractionProperty =
        AvaloniaProperty.Register<FractionBar, double>(nameof(Fraction));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<FractionBar, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<FractionBar, IBrush?>(nameof(Track));

    public static readonly StyledProperty<double> FillOpacityProperty =
        AvaloniaProperty.Register<FractionBar, double>(nameof(FillOpacity), 1.0);

    public double Fraction { get => GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush? Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public double FillOpacity { get => GetValue(FillOpacityProperty); set => SetValue(FillOpacityProperty, value); }

    static FractionBar()
    {
        AffectsRender<FractionBar>(FractionProperty, FillProperty, TrackProperty, FillOpacityProperty);
    }

    public FractionBar() => Height = 6;

    public override void Render(DrawingContext context)
    {
        var b = Bounds;
        if (Track != null)
            context.FillRectangle(Track, new Rect(0, 0, b.Width, b.Height));
        if (Fill != null && Fraction > 0)
        {
            var w = Math.Max(b.Width * 0.02, b.Width * Math.Min(1, Fraction));
            using (context.PushOpacity(FillOpacity))
                context.FillRectangle(Fill, new Rect(0, 0, w, b.Height));
        }
    }
}
