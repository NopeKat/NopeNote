using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NopeNote.Windows.Helpers;

namespace NopeNote.Windows.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private void ThemeSystem_Click(object sender, RoutedEventArgs e)
    {
        ThemeHelper.SetTheme(ElementTheme.Default);
    }

    private void ThemeLight_Click(object sender, RoutedEventArgs e)
    {
        ThemeHelper.SetTheme(ElementTheme.Light);
    }

    private void ThemeDark_Click(object sender, RoutedEventArgs e)
    {
        ThemeHelper.SetTheme(ElementTheme.Dark);
    }
}
