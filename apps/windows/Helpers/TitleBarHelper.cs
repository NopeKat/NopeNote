using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace NopeNote.Windows.Helpers;

public static class TitleBarHelper
{
    public static void ApplySystemThemeToCaptionButtons(Window window, ElementTheme theme)
    {
        var titleBar = window.AppWindow.TitleBar;

        var foreground = theme == ElementTheme.Dark ? Colors.White : Colors.Black;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = foreground;

        var hoverBackground = theme == ElementTheme.Dark
            ? Color.FromArgb(24, 255, 255, 255)
            : Color.FromArgb(24, 0, 0, 0);
        titleBar.ButtonHoverBackgroundColor = hoverBackground;
    }
}
