using Microsoft.UI.Xaml;
using NopeNote.Windows.Helpers;
using NopeNote.Windows.Services;
using System;
using System.Threading.Tasks;

namespace NopeNote.Windows;

public partial class App : Application
{
    private readonly AppServices _services;
    private MainWindow? _window;

    public App()
    {
        FileLog.Initialize();
        UnhandledException += App_UnhandledException;

        _services = AppServices.CreateDefault();
        LanguageHelper.ApplyStartupOverride(_services.Settings);
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        FileLog.Info("app started");
        IdleSynchronizer.Init();

        try
        {
            await EnsureWindowAsync();
        }
        catch (Exception exception)
        {
            FileLog.Error("startup failed", exception);
            throw;
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        FileLog.Error("unhandled exception", args.Exception);
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
