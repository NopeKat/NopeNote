using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
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
    private bool _updatingLanguageSelection;

    public SettingsPage()
    {
        InitializeComponent();
        AppThemeCard.Header = Strings.Get("SettingsAppThemeHeader");
        AppThemeCard.Description = Strings.Get("SettingsAppThemeDescription");
        VaultCard.Header = Strings.Get("SettingsVaultHeader");
        ExportCard.Header = Strings.Get("SettingsExportHeader");
        ExportCard.Description = Strings.Get("SettingsExportDescription");
        ExportButton.Content = Strings.Get("SettingsExportButton");
        VersionHistoryCard.Header = Strings.Get("SettingsVersionHistoryHeader");
        VersionHistoryCard.Description = Strings.Get("SettingsVersionHistoryDescription");
        AboutExpander.Header = Strings.Get("SettingsAboutHeader");
        AboutExpander.Description = Strings.Get("SettingsAboutDescription");
        LanguageCard.Header = Strings.Get("SettingsLanguageHeader");
        LanguageCard.Description = Strings.Get("SettingsLanguageDescription");
        UpdateThemeSelection();
        UpdateLanguageSelection();
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

    private async void LanguageMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingLanguageSelection || LanguageModeComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        string language = item.Tag?.ToString() ?? LanguageHelper.FollowSystem;
        if (language == LanguageHelper.SelectedLanguage)
        {
            return;
        }

        LanguageHelper.Select(language);

        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = Strings.Get("LanguageRestartDialogTitle"),
            Content = Strings.Get("LanguageRestartDialogBody"),
            PrimaryButtonText = Strings.Get("LanguageRestartDialogPrimary"),
            CloseButtonText = Strings.Get("CommonLater"),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            LanguageHelper.RestartApplication();
        }
    }

    private void UpdateLanguageSelection()
    {
        _updatingLanguageSelection = true;

        foreach (object item in LanguageModeComboBox.Items)
        {
            if (item is ComboBoxItem comboItem &&
                (comboItem.Tag?.ToString() ?? LanguageHelper.FollowSystem) == LanguageHelper.SelectedLanguage)
            {
                LanguageModeComboBox.SelectedItem = comboItem;
                break;
            }
        }

        _updatingLanguageSelection = false;
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

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        nint hwnd = WindowHelper.GetWindowHandle(WindowHelper.ActiveWindows.FirstOrDefault());
        if (hwnd == 0)
        {
            return;
        }

        FileSavePicker picker = new()
        {
            SuggestedFileName = $"nopenote-backup-{DateTime.Now:yyyyMMdd-HHmm}.zip",
        };
        picker.FileTypeChoices.Add(Strings.Get("SettingsExportFileType"), new List<string> { ".zip" });
        InitializeWithWindow.Initialize(picker, hwnd);

        global::Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        ExportButton.IsEnabled = false;
        try
        {
            await VaultExporter.ExportAsync(SettingsHelper.Current.VaultPath, file.Path);
            ContentDialog doneDialog = new()
            {
                XamlRoot = XamlRoot,
                Title = Strings.Get("SettingsExportDoneTitle"),
                Content = Strings.Format("SettingsExportDoneMessage", file.Path),
                CloseButtonText = Strings.Get("CommonClose"),
            };
            await doneDialog.ShowAsync();
        }
        catch (Exception exception)
        {
            ContentDialog failedDialog = new()
            {
                XamlRoot = XamlRoot,
                Title = Strings.Get("SettingsExportFailedTitle"),
                Content = exception.Message,
                CloseButtonText = Strings.Get("CommonClose"),
            };
            await failedDialog.ShowAsync();
        }
        finally
        {
            ExportButton.IsEnabled = true;
        }
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
