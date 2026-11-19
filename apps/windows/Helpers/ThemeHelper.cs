using Microsoft.UI.Xaml;

namespace NopeNote.Windows.Helpers;

public static class ThemeHelper
{
    private static ElementTheme _rootTheme = ElementTheme.Default;

    public static ElementTheme RootTheme
    {
        get => _rootTheme;
        private set
        {
            _rootTheme = value;
            SettingsHelper.Current.SelectedAppTheme = value;
            SettingsHelper.Current.Save();
        }
    }

    public static void Initialize()
    {
        _rootTheme = SettingsHelper.Current.SelectedAppTheme;
        Apply(_rootTheme);
    }

    public static void SetTheme(ElementTheme theme)
    {
        RootTheme = theme;
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
        foreach (var window in WindowHelper.ActiveWindows)
        {
            if (window.Content is FrameworkElement root)
            {
                root.RequestedTheme = theme;
            }
        }
    }
}
