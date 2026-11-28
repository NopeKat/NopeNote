using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private bool _updatingVerboseLog;
    private AppServices _services = null!;
    private AttachmentGcPlan? _attachmentPlan;

    public SettingsPage()
    {
        InitializeComponent();
        AppThemeCard.Header = Strings.Get("SettingsAppThemeHeader");
        AppThemeCard.Description = Strings.Get("SettingsAppThemeDescription");
        VaultCard.Header = Strings.Get("SettingsVaultHeader");
        LockCard.Header = Strings.Get("SettingsLockHeader");
        ExportCard.Header = Strings.Get("SettingsExportHeader");
        ExportCard.Description = Strings.Get("SettingsExportDescription");
        ExportButton.Content = Strings.Get("SettingsExportButton");
        VersionHistoryCard.Header = Strings.Get("SettingsVersionHistoryHeader");
        VersionHistoryCard.Description = Strings.Get("SettingsVersionHistoryDescription");
        AboutExpander.Header = Strings.Get("SettingsAboutHeader");
        AboutExpander.Description = Strings.Get("SettingsAboutDescription");
        LanguageCard.Header = Strings.Get("SettingsLanguageHeader");
        LanguageCard.Description = Strings.Get("SettingsLanguageDescription");
        AttachmentGcCard.Header = Strings.Get("SettingsAttachmentGcHeader");
        AttachmentGcCard.Description = Strings.Get("SettingsAttachmentGcDescription");
        AttachmentScanButton.Content = Strings.Get("SettingsAttachmentScanButton");
        AttachmentCleanButton.Content = Strings.Get("SettingsAttachmentCleanButton");
        AttachmentPurgeButton.Content = Strings.Get("SettingsAttachmentPurgeButton");
        AutomationProperties.SetName(AttachmentScanButton, Strings.Get("SettingsAttachmentScanButton"));
        AutomationProperties.SetName(AttachmentCleanButton, Strings.Get("SettingsAttachmentCleanButton"));
        AutomationProperties.SetName(AttachmentPurgeButton, Strings.Get("SettingsAttachmentPurgeButton"));
        UpdateThemeSelection();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is AppServices services)
        {
            _services = services;
        }

        UpdateLockState();
        UpdateLanguageSelection();
        VaultCard.Description = _services.Settings.VaultPath;

        _updatingGitToggle = true;
        GitToggle.IsOn = VaultSettings.Read(_services.Settings.VaultPath).GitEnabled;
        _updatingGitToggle = false;

        _updatingVerboseLog = true;
        VerboseLogToggle.IsOn = _services.Settings.VerboseLogging;
        _updatingVerboseLog = false;

        UpdateAttachmentGcState();
    }

    private void VerboseLogToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingVerboseLog)
        {
            return;
        }

        bool enabled = VerboseLogToggle.IsOn;
        _services.Settings.VerboseLogging = enabled;
        _services.Settings.SaveDeferred();
        FileLog.SetVerbose(enabled);
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

        ThemeHelper.SetTheme(_services.Settings, theme);
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

    private void LanguageMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingLanguageSelection || LanguageModeComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        string language = item.Tag?.ToString() ?? LanguageHelper.FollowSystem;
        if (language == LanguageHelper.SelectedLanguage(_services.Settings))
        {
            return;
        }

        // 语言立即生效，不再提示重启；主窗口会按新语言重建当前页和导航项。
        // 重建放在输入回调返回之后做，避免在原生弹出还在收拢时就拆掉宿主页面。
        LanguageModeComboBox.IsDropDownOpen = false;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Application.Current is App app)
            {
                app.ApplyLanguageChange(language);
            }
            else
            {
                LanguageHelper.Select(_services.Settings, language);
            }
        });
    }

    private void UpdateLanguageSelection()
    {
        _updatingLanguageSelection = true;

        foreach (object item in LanguageModeComboBox.Items)
        {
            if (item is ComboBoxItem comboItem &&
                (comboItem.Tag?.ToString() ?? LanguageHelper.FollowSystem) == LanguageHelper.SelectedLanguage(_services.Settings))
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

        _services.Settings.VaultPath = folder.Path;
        _services.Settings.Save();
        _services.Vault.Reset();
        VaultCard.Description = folder.Path;

        Frame.Navigate(typeof(NotesPage), new NotesNavigation(null, _services));
    }

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e)
    {
        string? folder = FileLog.EnsureFolder();
        if (folder is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true,
        });
    }

    private void UpdateLockState()
    {
        bool locked = _services.Settings.HasAppLock;
        LockCard.Description = locked
            ? Strings.Get("SettingsLockEnabled")
            : Strings.Get("SettingsLockDisabled");
        LockButton.Content = locked
            ? Strings.Get("SettingsLockManage")
            : Strings.Get("SettingsLockSet");
    }

    private async void LockButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_services.Settings.HasAppLock)
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

        ContentDialogResult result = await DialogHost.ShowAsync(dialog);
        if (result == ContentDialogResult.Primary)
        {
            _services.Settings.AppLockSalt = string.Empty;
            _services.Settings.AppLockHash = string.Empty;
            _services.Settings.Save();
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
            ContentDialogResult result = await DialogHost.ShowAsync(dialog);
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
            _services.Settings.AppLockSalt = salt;
            _services.Settings.AppLockHash = hash;
            _services.Settings.Save();
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
            await VaultExporter.ExportAsync(_services.Settings.VaultPath, file.Path);
            ContentDialog doneDialog = new()
            {
                XamlRoot = XamlRoot,
                Title = Strings.Get("SettingsExportDoneTitle"),
                Content = Strings.Format("SettingsExportDoneMessage", file.Path),
                CloseButtonText = Strings.Get("CommonClose"),
            };
            await DialogHost.ShowAsync(doneDialog);
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
            await DialogHost.ShowAsync(failedDialog);
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
        VaultOpenResult opened = await _services.Vault.GetVaultAsync();
        if (opened.Status != VaultOpenStatus.Opened)
        {
            _updatingGitToggle = true;
            GitToggle.IsOn = !enabled;
            _updatingGitToggle = false;
            return;
        }

        await _services.Vault.FlushCommitsAsync(TimeSpan.FromSeconds(10));
        VaultSettings.Write(_services.Settings.VaultPath, new VaultSettings(enabled));
        _services.Vault.Reset();
        await _services.Vault.GetVaultAsync();
        _services.Vault.NotifyDataChanged();
    }

    private void UpdateAttachmentGcState()
    {
        IVaultStore? store = _services.Vault.Store;
        bool writable = store is not null && store.Capabilities.Writable;
        AttachmentScanButton.IsEnabled = writable;
        AttachmentPurgeButton.IsEnabled = writable && store is not null && AttachmentGc.HasQuarantine(store);
        if (!writable)
        {
            AttachmentGcStatus.Text = Strings.Get("AttachmentGcReadOnly");
        }
    }

    private async void AttachmentScanButton_Click(object sender, RoutedEventArgs e)
    {
        IVaultStore? store = _services.Vault.Store;
        if (store is null)
        {
            return;
        }

        AttachmentScanButton.IsEnabled = false;
        try
        {
            AttachmentGcPlan plan = await Task.Run(() => AttachmentGc.Scan(store)).ConfigureAwait(true);
            _attachmentPlan = plan;
            AttachmentCleanButton.IsEnabled = plan.Count > 0;
            AttachmentGcStatus.Text = plan.Count == 0
                ? Strings.Get("AttachmentGcNone")
                : Strings.Format("AttachmentGcScanResult", plan.Count, FormatBytes(plan.Bytes));
        }
        catch (Exception exception)
        {
            AttachmentGcStatus.Text = exception.Message;
        }
        finally
        {
            AttachmentScanButton.IsEnabled = true;
        }
    }

    private async void AttachmentCleanButton_Click(object sender, RoutedEventArgs e)
    {
        IVaultStore? store = _services.Vault.Store;
        AttachmentGcPlan? plan = _attachmentPlan;
        if (store is null || plan is null || plan.Count == 0)
        {
            return;
        }

        ContentDialog confirm = new()
        {
            XamlRoot = XamlRoot,
            Title = Strings.Get("AttachmentGcConfirmTitle"),
            Content = Strings.Format("AttachmentGcConfirmBody", plan.Count, FormatBytes(plan.Bytes)),
            PrimaryButtonText = Strings.Get("SettingsAttachmentCleanButton"),
            CloseButtonText = Strings.Get("CommonCancel"),
        };

        if (await DialogHost.ShowAsync(confirm) != ContentDialogResult.Primary)
        {
            return;
        }

        AttachmentCleanButton.IsEnabled = false;
        try
        {
            int moved = await Task.Run(() => AttachmentGc.Quarantine(store, plan)).ConfigureAwait(true);
            foreach (AttachmentOrphan orphan in plan.Orphans)
            {
                await store.RegisterAttachmentRemovalAsync(orphan.Name).ConfigureAwait(true);
            }

            await store.FlushAsync().ConfigureAwait(true);
            _attachmentPlan = null;
            AttachmentGcStatus.Text = Strings.Format("AttachmentGcMoved", moved, FormatBytes(plan.Bytes));
            UpdateAttachmentGcState();
        }
        catch (Exception exception)
        {
            AttachmentGcStatus.Text = exception.Message;
            AttachmentCleanButton.IsEnabled = true;
        }
    }

    private async void AttachmentPurgeButton_Click(object sender, RoutedEventArgs e)
    {
        IVaultStore? store = _services.Vault.Store;
        if (store is null)
        {
            return;
        }

        ContentDialog confirm = new()
        {
            XamlRoot = XamlRoot,
            Title = Strings.Get("AttachmentGcPurgeTitle"),
            Content = Strings.Get("AttachmentGcPurgeBody"),
            PrimaryButtonText = Strings.Get("SettingsAttachmentPurgeButton"),
            CloseButtonText = Strings.Get("CommonCancel"),
        };

        if (await DialogHost.ShowAsync(confirm) != ContentDialogResult.Primary)
        {
            return;
        }

        AttachmentPurgeButton.IsEnabled = false;
        try
        {
            int deleted = await Task.Run(() => AttachmentGc.PurgeQuarantine(store)).ConfigureAwait(true);
            AttachmentGcStatus.Text = Strings.Format("AttachmentGcPurged", deleted);
            UpdateAttachmentGcState();
        }
        catch (Exception exception)
        {
            AttachmentGcStatus.Text = exception.Message;
            AttachmentPurgeButton.IsEnabled = true;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double kilobytes = bytes / 1024.0;
        if (kilobytes < 1024)
        {
            return $"{kilobytes:0.#} KB";
        }

        return $"{kilobytes / 1024.0:0.#} MB";
    }
}
