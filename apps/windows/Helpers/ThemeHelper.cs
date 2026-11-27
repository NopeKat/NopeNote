using Microsoft.UI.Xaml;

namespace NopeNote.Windows.Helpers;

public static class ThemeHelper
{
    private static ElementTheme _rootTheme = ElementTheme.Default;

    public static ElementTheme RootTheme
    {
        get => _rootTheme;
        private set => _rootTheme = value;
    }

    public static void Initialize(SettingsHelper settings)
    {
        _rootTheme = settings.SelectedAppTheme;
        Apply(_rootTheme);
    }

    public static void SetTheme(SettingsHelper settings, ElementTheme theme)
    {
        RootTheme = theme;
        settings.SelectedAppTheme = theme;
        settings.Save();
        Apply(theme);
    }

    public static bool IsDarkTheme()
    {
        return _rootTheme == ElementTheme.Dark ||
            (_rootTheme == ElementTheme.Default &&
             Application.Current.RequestedTheme == ApplicationTheme.Dark);
    }

    private static void Apply(ElementTheme theme)
    {
        foreach (Window window in WindowHelper.ActiveWindows)
        {
            if (window.Content is FrameworkElement root)
            {
                root.RequestedTheme = theme;
            }
        }
    }
}
