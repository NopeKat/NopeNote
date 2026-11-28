using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NopeNote.Core.Vault;
using NopeNote.Windows.Helpers;
using NopeNote.Windows.Services;

namespace NopeNote.Windows.Views;

public sealed class TrashItem
{
    public TrashItem(TrashNoteSummary summary)
    {
        Id = summary.Id;
        Title = NoteTitle.Display(summary.Title);
        IsAvailable = summary.IsAvailable;
        Details = Strings.Format(
            "TrashItemDetails",
            summary.DeletedAt.LocalDateTime.ToString("g"),
            summary.Updated.LocalDateTime.ToString("g"));
    }

    public Guid Id { get; }

    public string Title { get; }

    public bool IsAvailable { get; }

    public string Details { get; }
}

public sealed partial class TrashPage : Page
{
    private IReadOnlyList<TrashItem> _items = Array.Empty<TrashItem>();
    private bool _isLoading;
    private IVaultStore? _loadedStore;
    private int _loadedVersion = -1;
    private AppServices _services = null!;

    public TrashPage()
    {
        InitializeComponent();
        Loaded += TrashPage_Loaded;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is AppServices services)
        {
            _services = services;
        }
    }

    private async void TrashPage_Loaded(object sender, RoutedEventArgs e)
    {
        IVaultStore? store = _services.Vault.Store;
        if (_loadedStore is not null &&
            ReferenceEquals(_loadedStore, store) &&
            _loadedVersion == _services.Vault.DataVersion)
        {
            return;
        }

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        SetLoading(true);

        try
        {
            IVaultStore? store = _services.Vault.Store;
            if (store is null)
            {
                ShowStatus(
                    Strings.Get("TrashVaultMissingTitle"),
                    Strings.Get("TrashVaultMissingMessage"),
                    InfoBarSeverity.Error);
                return;
            }

            IReadOnlyList<TrashNoteSummary> summaries = await store.ListTrashAsync();
            _items = summaries.Select(summary => new TrashItem(summary)).ToArray();
            TrashList.ItemsSource = _items;
            TrashList.SelectedItems.Clear();
            _loadedStore = store;
            _loadedVersion = _services.Vault.DataVersion;
            UpdateEmptyState();

            if (!store.Capabilities.Writable)
            {
                ShowStatus(
                    Strings.Get("TrashReadOnlyTitle"),
                    Strings.Get("TrashReadOnlyMessage"),
                    InfoBarSeverity.Warning);
            }
            else
            {
                StatusInfoBar.IsOpen = false;
            }
        }
        catch (Exception exception)
        {
            ShowStatus(Strings.Get("TrashLoadFailedTitle"), exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void SetLoading(bool isLoading)
    {
        _isLoading = isLoading;
        LoadingRing.IsActive = isLoading;
        LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        TrashList.Visibility = isLoading ? Visibility.Collapsed : Visibility.Visible;

        UpdateEmptyState();
        UpdateActionState();
    }

    private void UpdateActionState()
    {
        bool writable = _services.Vault.IsWritable;
        bool hasAvailableSelection = TrashList.SelectedItems.OfType<TrashItem>().Any(item => item.IsAvailable);
        RestoreButton.IsEnabled = !_isLoading && writable && hasAvailableSelection;
        PurgeButton.IsEnabled = !_isLoading && writable && TrashList.SelectedItems.Count > 0;
        EmptyTrashButton.IsEnabled = !_isLoading && writable && _items.Count > 0;
    }

    private void UpdateEmptyState()
    {
        EmptyStatePanel.Visibility = !_isLoading && _items.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // 本页列表已就地更新，同时抬高版本号，让其他缓存页面下次进入时重新加载。
    private void MarkDataChanged()
    {
        _services.Vault.NotifyDataChanged();
        _loadedVersion = _services.Vault.DataVersion;
    }

    private void TrashList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateActionState();
    }

    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        TrashItem[] selected = TrashList.SelectedItems
            .OfType<TrashItem>()
            .Where(item => item.IsAvailable)
            .ToArray();

        IVaultStore? store = _services.Vault.Store;
        if (selected.Length == 0 || store is null)
        {
            return;
        }

        int failed = 0;
        foreach (TrashItem item in selected)
        {
            try
            {
                await store.RestoreNoteAsync(item.Id);
            }
            catch (Exception)
            {
                failed++;
            }
        }

        MarkDataChanged();
        await LoadAsync();

        if (failed > 0)
        {
            ShowStatus(
                Strings.Get("TrashRestoreFailedTitle"),
                Strings.Format("TrashBatchRestoreFailedMessage", failed),
                InfoBarSeverity.Warning);
        }
    }

    private async void PurgeButton_Click(object sender, RoutedEventArgs e)
    {
        TrashItem[] selected = TrashList.SelectedItems.OfType<TrashItem>().ToArray();
        IVaultStore? store = _services.Vault.Store;
        if (selected.Length == 0 || store is null)
        {
            return;
        }

        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = Strings.Get("TrashPurgeDialogTitle"),
            Content = Strings.Get("TrashPurgeSelectedDialogBody"),
            PrimaryButtonText = Strings.Get("TrashPurgeDialogPrimary"),
            CloseButtonText = Strings.Get("CommonCancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await DialogHost.ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        int failed = await PurgeAsync(store, selected);
        if (failed > 0)
        {
            ShowStatus(
                Strings.Get("TrashPurgeFailedTitle"),
                Strings.Format("TrashBatchPurgeFailedMessage", failed),
                InfoBarSeverity.Warning);
        }
    }

    private async void EmptyTrashButton_Click(object sender, RoutedEventArgs e)
    {
        IVaultStore? store = _services.Vault.Store;
        if (_items.Count == 0 || store is null)
        {
            return;
        }

        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = Strings.Get("TrashEmptyDialogTitle"),
            Content = Strings.Get("TrashEmptyDialogBody"),
            PrimaryButtonText = Strings.Get("TrashEmptyDialogPrimary"),
            CloseButtonText = Strings.Get("CommonCancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await DialogHost.ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        int failed = await PurgeAsync(store, _items);
        if (failed > 0)
        {
            ShowStatus(
                Strings.Get("TrashPurgeFailedTitle"),
                Strings.Format("TrashBatchPurgeFailedMessage", failed),
                InfoBarSeverity.Warning);
        }
    }

    private async Task<int> PurgeAsync(IVaultStore store, IReadOnlyList<TrashItem> items)
    {
        int failed = 0;
        foreach (TrashItem item in items)
        {
            try
            {
                await store.PurgeNoteAsync(item.Id);
            }
            catch (Exception)
            {
                failed++;
            }
        }

        MarkDataChanged();
        await LoadAsync();
        return failed;
    }

    private void OpenNotesButton_Click(object sender, RoutedEventArgs e)
    {
        Frame?.Navigate(typeof(NotesPage), _services);
    }

    private void ShowStatus(string title, string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }
}
