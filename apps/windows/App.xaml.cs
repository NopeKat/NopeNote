using Microsoft.UI.Xaml;
using NopeNote.Windows.Helpers;
using System.Threading.Tasks;

namespace NopeNote.Windows;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        LanguageHelper.ApplyStartupOverride();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        IdleSynchronizer.Init();
        await EnsureWindowAsync();
    }

    private async Task EnsureWindowAsync()
    {
        _window = new MainWindow();
        WindowHelper.TrackWindow(_window);
        ThemeHelper.Initialize();
        _window.Activate();

        if (!await _window.EnsureUnlockedAsync())
        {
            _window.Close();
            return;
        }

        _window.InitializeNavigation();
    }
}
