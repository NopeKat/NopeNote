using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using NopeNote.Core.Vault;
using NopeNote.Windows.Helpers;
using NopeNote.Windows.Services;
using global::Windows.Storage.Pickers;
using WinRT.Interop;

namespace NopeNote.Windows.Views;

public sealed partial class SettingsPage : Page
{
    private bool _updatingThemeSelection;
    private bool _updatingGitToggle;

    public SettingsPage()
    {
        InitializeComponent();
        AppThemeCard.Header = Strings.Get("SettingsAppThemeHeader");
        AppThemeCard.Description = Strings.Get("SettingsAppThemeDescription");
        VaultCard.Header = Strings.Get("SettingsVaultHeader");
        VersionHistoryCard.Header = Strings.Get("SettingsVersionHistoryHeader");
        VersionHistoryCard.Description = Strings.Get("SettingsVersionHistoryDescription");
        AboutExpander.Header = Strings.Get("SettingsAboutHeader");
        AboutExpander.Description = Strings.Get("SettingsAboutDescription");
        UpdateThemeSelection();
        VaultCard.Description = SettingsHelper.Current.VaultPath;

        _updatingGitToggle = true;
        GitToggle.IsOn = VaultSettings.Read(SettingsHelper.Current.VaultPath).GitEnabled;
        _updatingGitToggle = false;
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

    private async void GitToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingGitToggle)
        {
            return;
        }

        bool enabled = GitToggle.IsOn;
        VaultOpenResult opened = await VaultService.Current.GetVaultAsync();
        if (opened.Status != VaultOpenStatus.Opened)
        {
            _updatingGitToggle = true;
            GitToggle.IsOn = !enabled;
            _updatingGitToggle = false;
            return;
        }

        VaultSettings.Write(SettingsHelper.Current.VaultPath, new VaultSettings(enabled));
        VaultService.Current.Reset();
    }
}
