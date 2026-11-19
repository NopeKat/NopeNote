using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NopeNote.Windows.Helpers;

namespace NopeNote.Windows.Views;

public sealed partial class SettingsPage : Page
{
    private bool _updatingThemeSelection;

    public SettingsPage()
    {
        InitializeComponent();
        UpdateThemeSelection();
    }

    private void ThemeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingThemeSelection || ThemeModeComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var theme = item.Tag?.ToString() switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        ThemeHelper.SetTheme(theme);
    }

    private void UpdateThemeSelection()
    {
        _updatingThemeSelection = true;

        var tag = ThemeHelper.RootTheme switch
        {
            ElementTheme.Light => "Light",
            ElementTheme.Dark => "Dark",
            _ => "Default"
        };

        foreach (var item in ThemeModeComboBox.Items)
        {
            if (item is ComboBoxItem comboItem && comboItem.Tag?.ToString() == tag)
            {
                ThemeModeComboBox.SelectedItem = comboItem;
                break;
            }
        }

        _updatingThemeSelection = false;
    }
}
