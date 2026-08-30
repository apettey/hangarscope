using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HangarScope.Services;
using HangarScope.ViewModels;
using HangarScope.Views;

namespace HangarScope;

public partial class App : Application
{
    private SyncService? _sync;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _sync = new SyncService();
            var vm = new MainViewModel(_sync);
            desktop.MainWindow = new MainWindow { DataContext = vm };
            desktop.Exit += (_, _) => _sync.Dispose();
            _sync.Start();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
