using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        LockCard.Header = Strings.Get("SettingsLockHeader");
        UpdateLockState();
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

    private void UpdateLockState()
    {
        bool locked = SettingsHelper.Current.HasAppLock;
        LockCard.Description = locked
            ? Strings.Get("SettingsLockEnabled")
            : Strings.Get("SettingsLockDisabled");
        LockButton.Content = locked
            ? Strings.Get("SettingsLockManage")
            : Strings.Get("SettingsLockSet");
    }

    private async void LockButton_Click(object sender, RoutedEventArgs e)
    {
        if (!SettingsHelper.Current.HasAppLock)
        {
            await ShowSetPasswordDialogAsync();
            return;
        }

        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = Strings.Get("SettingsLockManageTitle"),
            Content = Strings.Get("SettingsLockManageBody"),
            PrimaryButtonText = Strings.Get("SettingsLockRemove"),
            SecondaryButtonText = Strings.Get("SettingsLockChange"),
            CloseButtonText = Strings.Get("CommonCancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            SettingsHelper.Current.AppLockSalt = string.Empty;
            SettingsHelper.Current.AppLockHash = string.Empty;
            SettingsHelper.Current.Save();
            UpdateLockState();
        }
        else if (result == ContentDialogResult.Secondary)
        {
            await ShowSetPasswordDialogAsync();
        }
    }

    private async Task ShowSetPasswordDialogAsync()
    {
        PasswordBox password = new() { PlaceholderText = Strings.Get("SettingsLockPassword") };
        PasswordBox confirmation = new() { PlaceholderText = Strings.Get("SettingsLockConfirm") };
        TextBlock error = new()
        {
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Crimson),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        StackPanel panel = new() { Spacing = 8 };
        panel.Children.Add(password);
        panel.Children.Add(confirmation);
        panel.Children.Add(error);

        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = Strings.Get("SettingsLockSetTitle"),
            Content = panel,
            PrimaryButtonText = Strings.Get("SettingsLockSave"),
            CloseButtonText = Strings.Get("CommonCancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        while (true)
        {
            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            string value = password.Password;
            if (value.Length < 4)
            {
                error.Text = Strings.Get("SettingsLockTooShort");
                error.Visibility = Visibility.Visible;
                await Task.Delay(50);
                continue;
            }

            if (!string.Equals(value, confirmation.Password, StringComparison.Ordinal))
            {
                error.Text = Strings.Get("SettingsLockMismatch");
                error.Visibility = Visibility.Visible;
                await Task.Delay(50);
                continue;
            }

            (string salt, string hash) = PasswordHasher.Create(value);
            SettingsHelper.Current.AppLockSalt = salt;
            SettingsHelper.Current.AppLockHash = hash;
            SettingsHelper.Current.Save();
            UpdateLockState();
            return;
        }
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

        await VaultService.Current.FlushCommitsAsync(TimeSpan.FromSeconds(10));
        VaultSettings.Write(SettingsHelper.Current.VaultPath, new VaultSettings(enabled));
        VaultService.Current.Reset();
        await VaultService.Current.GetVaultAsync();
        VaultService.Current.NotifyDataChanged();
    }
}
