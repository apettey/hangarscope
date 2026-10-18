using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HangarScope.ViewModels;

namespace HangarScope.Views;

public partial class FitPickerView : UserControl
{
    /// <summary>Flyout use: hides the library-only actions (tidy, delete).</summary>
    public static readonly StyledProperty<bool> CompactProperty = AvaloniaProperty.Register<FitPickerView, bool>(nameof(Compact));

    public bool Compact
    {
        get => GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    /// <summary>Raised after a fit was used or an action started, so a hosting flyout can close.</summary>
    public event EventHandler? Done;

    public FitPickerView()
    {
        InitializeComponent();
        // arrows move the highlight from the search box, Enter uses it
        Search.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        List.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        List.DoubleTapped += (_, _) => Activate();
        List.SelectionChanged += (_, _) => { if (List.SelectedItem is { } item) List.ScrollIntoView(item); };
        AttachedToVisualTree += (_, _) => { if (Compact) Dispatcher.UIThread.Post(() => Search.Focus(), DispatcherPriority.Input); };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CompactProperty) TidyButton.IsVisible = DeleteButton.IsVisible = !Compact;
    }

    private FitPickerVm? Vm => DataContext as FitPickerVm;

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;
        switch (e.Key)
        {
            case Key.Down: vm.Move(1); e.Handled = true; break;
            case Key.Up: vm.Move(-1); e.Handled = true; break;
            case Key.PageDown: vm.Move(10); e.Handled = true; break;
            case Key.PageUp: vm.Move(-10); e.Handled = true; break;
            case Key.Enter: Activate(); e.Handled = true; break;
        }
    }

    private void Activate()
    {
        if (Vm?.Activate() == true) Done?.Invoke(this, EventArgs.Empty);
    }

    private void Used(object? sender, RoutedEventArgs e) => Done?.Invoke(this, EventArgs.Empty);
}
