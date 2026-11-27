using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace NopeNote.Windows.Helpers;

public static class BackdropHelper
{
    /// <summary>
    /// 项目默认禁用 Mica 与 Acrylic，窗口一律使用纯色背景。
    /// </summary>
    public static void Apply(Window window)
    {
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
