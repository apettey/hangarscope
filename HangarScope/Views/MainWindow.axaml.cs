using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace HangarScope.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // --snapshot=<file.png> renders the window to a PNG shortly after start and exits (doc screenshot automation)
        var snap = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--snapshot="));
        if (snap != null) Opened += (_, _) => DispatcherTimer.RunOnce(() => SaveSnapshot(snap["--snapshot=".Length..]), TimeSpan.FromMilliseconds(2500));
    }

    private void SaveSnapshot(string path)
    {
        try
        {
            var scale = RenderScaling;
            var size = new PixelSize((int)(Bounds.Width * scale), (int)(Bounds.Height * scale));
            using var bmp = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
            bmp.Render(this);
            bmp.Save(path, new PngBitmapEncoderOptions());
        }
        finally { Close(); }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void TitleBar_DoubleTapped(object? sender, TappedEventArgs e) => ToggleMaximize();

    private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
