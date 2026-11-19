using Microsoft.UI.Xaml;

namespace NopeNote.Windows.Helpers;

public static class ThemeHelper
{
    private static Window? _window;

    public static ElementTheme RequestedTheme { get; private set; } = ElementTheme.Default;

    public static void Initialize(Window window)
    {
        _window = window;
        Apply(RequestedTheme);
    }

    public static void SetTheme(ElementTheme theme)
    {
        RequestedTheme = theme;
        Apply(theme);
    }

    private static void Apply(ElementTheme theme)
    {
        if (_window?.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme;
        }
    }
}
