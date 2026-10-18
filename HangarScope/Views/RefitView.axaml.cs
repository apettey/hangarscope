using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HangarScope.ViewModels;

namespace HangarScope.Views;

public partial class RefitView : UserControl
{
    public RefitView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.CopyToClipboard = async text =>
                {
                    if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) await cb.SetTextAsync(text);
                };
        };
        // --fit-flyout opens the IN-GAME FIT ▾ picker (doc screenshot automation)
        if (Environment.GetCommandLineArgs().Contains("--fit-flyout"))
            AttachedToVisualTree += (_, _) => DispatcherTimer.RunOnce(() => FitButton.Flyout?.ShowAt(FitButton), TimeSpan.FromMilliseconds(800));
        // --refit-scroll=orders|lists scrolls to the work orders or the pickup/shopping lists (doc screenshot automation)
        var scroll = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--refit-scroll="));
        if (scroll != null)
            AttachedToVisualTree += (_, _) => DispatcherTimer.RunOnce(() =>
            {
                Control target = scroll.EndsWith("lists") ? ListsAnchor : OrdersAnchor;
                PlanScroll.Offset = new Vector(0, Math.Max(0, target.TranslatePoint(new Point(0, 0), PlanScroll.Content as Visual ?? PlanScroll)?.Y ?? 0));
            }, TimeSpan.FromMilliseconds(600));
    }

    private void CloseFitFlyout(object? sender, EventArgs e) => FitButton.Flyout?.Hide();

    private void CloseFlyouts(object? sender, RoutedEventArgs e)
    {
        FitButton.Flyout?.Hide();
        ShipButton.Flyout?.Hide();
    }
}
