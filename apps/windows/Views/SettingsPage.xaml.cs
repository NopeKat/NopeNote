using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using NopeNote.Windows.Helpers;
using NopeNote.Windows.Services;
using global::Windows.Storage.Pickers;
using WinRT.Interop;

namespace NopeNote.Windows.Views;

public sealed partial class SettingsPage : Page
{
    private bool _updatingThemeSelection;

    public SettingsPage()
    {
        InitializeComponent();
        UpdateThemeSelection();
        VaultCard.Description = SettingsHelper.Current.VaultPath;
    }

    private void ThemeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingThemeSelection || ThemeModeComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        ElementTheme theme = item.Tag?.ToString() switch
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

        string tag = ThemeHelper.RootTheme switch
        {
            ElementTheme.Light => "Light",
            ElementTheme.Dark => "Dark",
            _ => "Default"
        };

        foreach (object item in ThemeModeComboBox.Items)
        {
            if (item is ComboBoxItem comboItem && comboItem.Tag?.ToString() == tag)
            {
                ThemeModeComboBox.SelectedItem = comboItem;
                break;
            }
        }

        _updatingThemeSelection = false;
    }

    private async void ChangeVaultButton_Click(object sender, RoutedEventArgs e)
    {
        nint hwnd = WindowHelper.GetWindowHandle(WindowHelper.ActiveWindows.FirstOrDefault());
        if (hwnd == 0)
        {
            return;
        }

        FolderPicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, hwnd);

        global::Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        SettingsHelper.Current.VaultPath = folder.Path;
        SettingsHelper.Current.Save();
        VaultService.Current.Reset();
        VaultCard.Description = folder.Path;

        Frame.Navigate(typeof(NotesPage));
    }
}
