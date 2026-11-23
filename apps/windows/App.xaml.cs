using Microsoft.UI.Xaml;
using NopeNote.Windows.Helpers;

namespace NopeNote.Windows;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        LanguageHelper.ApplyStartupOverride();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        IdleSynchronizer.Init();
        EnsureWindow();
    }

    private void EnsureWindow()
    {
        _window = new MainWindow();
        WindowHelper.TrackWindow(_window);
        ThemeHelper.Initialize();
        _window.InitializeNavigation();
        _window.Activate();
    }
}
