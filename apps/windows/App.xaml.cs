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
        FileLog.SetVerbose(_services.Settings.VerboseLogging);
        LanguageHelper.Apply(_services.Settings);
    }

    /// <summary>切换语言后立即生效：改运行时覆盖值并让主窗口按新语言重建界面，不重启进程。</summary>
    internal void ApplyLanguageChange(string language)
    {
        LanguageHelper.Select(_services.Settings, language);
        LanguageHelper.Apply(_services.Settings);
        Strings.Reload();
        _window?.ReloadLanguageUi();
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
        // Keep the process alive after a UI callback fault; every known async
        // entry point now catches locally, so this is the last-resort guard.
        args.Handled = true;
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
