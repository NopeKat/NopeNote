using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace NopeNote.Windows.Helpers;

public static class BackdropHelper
{
    public static void Apply(Window window)
    {
        if (MicaController.IsSupported())
        {
            window.SystemBackdrop = new MicaBackdrop();
            return;
        }

        if (DesktopAcrylicController.IsSupported())
        {
            window.SystemBackdrop = new DesktopAcrylicBackdrop();
            return;
        }

        window.SystemBackdrop = null;
        if (window.Content is Panel root)
        {
            root.Background = GetSolidFallbackBrush();
        }
    }

    private static Brush GetSolidFallbackBrush()
    {
        if (Application.Current.Resources.TryGetValue("SolidBackgroundFillColorBaseBrush", out object? value)
            && value is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(ThemeHelper.IsDarkTheme()
            ? Microsoft.UI.Colors.Black
            : Microsoft.UI.Colors.White);
    }
}
