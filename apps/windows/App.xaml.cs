using Microsoft.UI.Xaml;
using NopeNote.Windows.Helpers;
using NopeNote.Windows.Services;
using System.Threading.Tasks;

namespace NopeNote.Windows;

public partial class App : Application
{
    private readonly AppServices _services;
    private MainWindow? _window;

    public App()
    {
        _services = AppServices.CreateDefault();
        LanguageHelper.ApplyStartupOverride(_services.Settings);
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        IdleSynchronizer.Init();
        await EnsureWindowAsync();
    }

    private async Task EnsureWindowAsync()
    {
        _window = new MainWindow(_services);
        WindowHelper.TrackWindow(_window);
        ThemeHelper.Initialize(_services.Settings);
        _window.Activate();

        if (!await _window.EnsureUnlockedAsync())
        {
            _window.Close();
            return;
        }

        _window.InitializeNavigation();
    }
}
