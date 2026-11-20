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
            root.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }
}
