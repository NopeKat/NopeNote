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
        Title = summary.Title;
        Details = Strings.Format(
            "TrashItemDetails",
            summary.DeletedAt.LocalDateTime.ToString("g"),
            summary.Updated.LocalDateTime.ToString("g"));
    }

    public Guid Id { get; }

    public string Title { get; }

    public string Details { get; }
}

public sealed partial class TrashPage : Page
{
    private IReadOnlyList<TrashItem> _items = Array.Empty<TrashItem>();

    public TrashPage()
    {
        InitializeComponent();
        Loaded += TrashPage_Loaded;
    }

    private async void TrashPage_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        IVaultStore? store = VaultService.Current.Store;
        if (store is null)
        {
            ShowStatus(
                Strings.Get("TrashVaultMissingTitle"),
                Strings.Get("TrashVaultMissingMessage"),
                InfoBarSeverity.Error);
            return;
        }

        try
        {
            IReadOnlyList<TrashNoteSummary> summaries = await store.ListTrashAsync();
            _items = summaries.Select(summary => new TrashItem(summary)).ToArray();
            TrashList.ItemsSource = _items;
            TrashList.SelectedItem = null;

            EmptyStateTextBlock.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

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
    }

    private void TrashList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool writable = VaultService.Current.IsWritable;
        bool hasSelection = TrashList.SelectedItem is TrashItem;

        RestoreButton.IsEnabled = writable && hasSelection;
        PurgeButton.IsEnabled = writable && hasSelection;
    }

    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (TrashList.SelectedItem is not TrashItem item)
        {
            return;
        }

        IVaultStore? store = VaultService.Current.Store;
        if (store is null)
        {
            return;
        }

        try
        {
            await store.RestoreNoteAsync(item.Id);
            await LoadAsync();
        }
        catch (Exception exception)
        {
            ShowStatus(Strings.Get("TrashRestoreFailedTitle"), exception.Message, InfoBarSeverity.Error);
        }
    }

    private async void PurgeButton_Click(object sender, RoutedEventArgs e)
    {
        if (TrashList.SelectedItem is not TrashItem item)
        {
            return;
        }

        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = Strings.Get("TrashPurgeDialogTitle"),
            Content = Strings.Get("TrashPurgeDialogBody"),
            PrimaryButtonText = Strings.Get("TrashPurgeDialogPrimary"),
            CloseButtonText = Strings.Get("CommonCancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        IVaultStore? store = VaultService.Current.Store;
        if (store is null)
        {
            return;
        }

        try
        {
            await store.PurgeNoteAsync(item.Id);
            await LoadAsync();
        }
        catch (Exception exception)
        {
            ShowStatus(Strings.Get("TrashPurgeFailedTitle"), exception.Message, InfoBarSeverity.Error);
        }
    }

    private void ShowStatus(string title, string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }
}
