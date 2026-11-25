using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Collections.Generic;

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
}
