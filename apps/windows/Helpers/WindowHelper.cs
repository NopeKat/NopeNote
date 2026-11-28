using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using Windows.Graphics;

namespace NopeNote.Windows.Helpers;

public static class WindowHelper
{
    public static IReadOnlyList<Window> ActiveWindows => ActiveWindowList;

    private static readonly List<Window> ActiveWindowList = new();

    public static nint GetWindowHandle(Window? window)
        => window is null ? 0 : WinRT.Interop.WindowNative.GetWindowHandle(window);

    public static void TrackWindow(Window window)
    {
        window.Closed += (_, _) => ActiveWindowList.Remove(window);
        ActiveWindowList.Add(window);
        ThemeHelper.ApplyToWindow(window);
    }

    public static void SetWindowMinSize(Window window, double width, double height)
    {
        if (window.Content is not FrameworkElement content || content.XamlRoot is null)
        {
            return;
        }

        if (window.AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        double scale = content.XamlRoot.RasterizationScale;
        presenter.PreferredMinimumWidth = (int)(width * scale);
        presenter.PreferredMinimumHeight = (int)(height * scale);
    }

    /// <summary>Restores the last normal window size. Returns false when nothing usable is saved,
    /// so the caller can fall back to maximizing.</summary>
    public static bool TryApplySavedSize(Window window, SettingsHelper settings)
    {
        if (settings.WindowWidth < 640 || settings.WindowHeight < 500)
        {
            return false;
        }

        if (window.Content is not FrameworkElement content || content.XamlRoot is null)
        {
            return false;
        }

        double scale = content.XamlRoot.RasterizationScale;
        window.AppWindow.Resize(new SizeInt32(
            (int)Math.Round(settings.WindowWidth * scale),
            (int)Math.Round(settings.WindowHeight * scale)));
        return true;
    }

    /// <summary>Saves the current window size unless it is maximized, so the next launch
    /// reopens at the size the user actually chose.</summary>
    public static void SaveSize(Window window, SettingsHelper settings)
    {
        if (window.Content is not FrameworkElement content || content.XamlRoot is null)
        {
            return;
        }

        if (window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized })
        {
            return;
        }

        double scale = content.XamlRoot.RasterizationScale;
        settings.WindowWidth = window.AppWindow.Size.Width / scale;
        settings.WindowHeight = window.AppWindow.Size.Height / scale;
        settings.SaveDeferred();
    }
}
