using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NopeNote.Core.Vault;
using NopeNote.Windows.Services;

namespace NopeNote.Windows.Views;

public sealed class TrashItem
{
    public TrashItem(TrashNoteSummary summary)
    {
        Id = summary.Id;
        Title = summary.Title;
        Details = $"Deleted {summary.DeletedAt.LocalDateTime:g} · Updated {summary.Updated.LocalDateTime:g}";
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
            ShowStatus("Vault unavailable", "Open the notes page first to load the vault.", InfoBarSeverity.Error);
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
                ShowStatus("Vault opened read-only", "Restoring or deleting notes is disabled.", InfoBarSeverity.Warning);
            }
            else
            {
                StatusInfoBar.IsOpen = false;
            }
        }
        catch (Exception exception)
        {
            ShowStatus("Trash could not be loaded", exception.Message, InfoBarSeverity.Error);
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
            ShowStatus("Note could not be restored", exception.Message, InfoBarSeverity.Error);
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
            Title = "Delete permanently",
            Content = "This removes the note and its history snapshots. Continue?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
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
            ShowStatus("Note could not be deleted", exception.Message, InfoBarSeverity.Error);
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
