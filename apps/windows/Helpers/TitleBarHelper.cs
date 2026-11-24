using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace NopeNote.Windows.Helpers;

public static class TitleBarHelper
{
    private static readonly AccessibilitySettings AccessibilitySettings = new();

    public static void ApplySystemThemeToCaptionButtons(Window window, ElementTheme theme)
    {
        AppWindowTitleBar titleBar = window.AppWindow.TitleBar;

        if (AccessibilitySettings.HighContrast)
        {
            ResetCaptionButtonColors(titleBar);
            return;
        }

        Color foreground = theme == ElementTheme.Dark ? Colors.White : Colors.Black;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = foreground;

        Color hoverBackground = theme == ElementTheme.Dark
            ? Color.FromArgb(24, 255, 255, 255)
            : Color.FromArgb(24, 0, 0, 0);
        titleBar.ButtonHoverBackgroundColor = hoverBackground;
    }

    private static void ResetCaptionButtonColors(AppWindowTitleBar titleBar)
    {
        titleBar.ButtonBackgroundColor = null;
        titleBar.ButtonForegroundColor = null;
        titleBar.ButtonHoverBackgroundColor = null;
        titleBar.ButtonHoverForegroundColor = null;
        titleBar.ButtonPressedBackgroundColor = null;
        titleBar.ButtonPressedForegroundColor = null;
        titleBar.ButtonInactiveBackgroundColor = null;
        titleBar.ButtonInactiveForegroundColor = null;
    }
}
