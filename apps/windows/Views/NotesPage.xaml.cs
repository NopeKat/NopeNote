using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NopeNote.Core.Vault;
using NopeNote.Windows.Helpers;
using NopeNote.Windows.Models;
using NopeNote.Windows.Services;
using NopeNote.Windows.ViewModels;
using System;
using System.Threading.Tasks;

namespace NopeNote.Windows.Views;

public sealed partial class NotesPage : Page
{
    private enum RetryOperation
    {
        None,
        LoadNotes,
        SaveNote,
        DeleteNote
    }

    private bool _isSaving;
    private bool _isLoadingNotes;
    private bool _isSearchSubscribed;
    private bool _loadFailed;
    private Note? _pendingSaveNote;
    private Note? _retryNote;
    private RetryOperation _retryOperation = RetryOperation.None;

    public NotesPage()
    {
        ViewModel = new NotesViewModel();
        InitializeComponent();
        Loaded += NotesPage_Loaded;
        SizeChanged += NotesPage_SizeChanged;
        Unloaded += NotesPage_Unloaded;
    }

    public NotesViewModel ViewModel { get; }

    public Note? SelectedNote { get; private set; }

    private static double NarrowWidthThreshold =>
        (double)Microsoft.UI.Xaml.Application.Current.Resources["MasterDetailBreakpointWidth"];

    private void NotesPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyLayout(e.NewSize.Width < NarrowWidthThreshold);
    }

    private void NotesPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_isSearchSubscribed)
        {
            NoteSearchState.QueryChanged -= OnSearchQueryChanged;
            _isSearchSubscribed = false;
        }
    }

    private async void OnSearchQueryChanged(object? sender, EventArgs e)
    {
        await EnsureNotesLoadedAsync();
        ApplySearch();
    }

    private async void NotesPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isSearchSubscribed)
        {
            NoteSearchState.QueryChanged += OnSearchQueryChanged;
            _isSearchSubscribed = true;
        }

        ApplyLayout(ActualWidth < NarrowWidthThreshold);
        await EnsureNotesLoadedAsync();
        ApplySearch();
    }

    private async Task EnsureNotesLoadedAsync()
    {
        if (ViewModel.IsLoaded || _loadFailed || _isLoadingNotes)
        {
            return;
        }

        _isLoadingNotes = true;
        UpdateListState();

        try
        {
            VaultOpenResult opened = await VaultService.Current.GetVaultAsync();
            if (opened.Store is null)
            {
                _loadFailed = true;
                NewNoteButton.IsEnabled = false;
                ShowError(
                    "Vault could not be opened",
                    $"{opened.Message} Choose a different vault folder in settings, then select Retry.",
                    RetryOperation.LoadNotes,
                    null);
                return;
            }

            ViewModel.Attach(opened.Store);
            await ViewModel.LoadAsync();
            ApplyVaultState(opened);
        }
        catch (Exception exception)
        {
            _loadFailed = true;
            ShowError(
                "Notes could not be loaded",
                $"{Describe(exception)} Check access to the vault folder, then select Retry.",
                RetryOperation.LoadNotes,
                null);
        }
        finally
        {
            _isLoadingNotes = false;
            UpdateListState();
        }
    }

    private void ApplyVaultState(VaultOpenResult opened)
    {
        bool writable = opened.Store?.Capabilities.Writable == true;
        NewNoteButton.IsEnabled = writable;

        if (opened.Status != VaultOpenStatus.Opened)
        {
            ShowError(
                "Vault opened read-only",
                opened.Message,
                RetryOperation.None,
                null,
                InfoBarSeverity.Warning);
        }
    }

    private void ApplySearch()
    {
        ViewModel.ApplyFilter(NoteSearchState.Query);
        UpdateListState();
    }

    private async void NewNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!VaultService.Current.IsWritable)
        {
            ShowError(
                "Vault is read-only",
                "Notes cannot be created while the vault is open read-only.",
                RetryOperation.None,
                null,
                InfoBarSeverity.Warning);
            return;
        }

        Note note = new Note();
        ViewModel.Add(note);
        ApplySearch();
        SelectNote(note);
        NoteEditor.Focus(FocusState.Programmatic);
        await SaveNoteAsync(note);
    }

    private async void DeleteNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNote is null)
        {
            return;
        }

        ContentDialog dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete note",
            Content = "This note will be moved to the trash. Continue?",
            PrimaryButtonText = "Move to trash",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || SelectedNote is null)
        {
            return;
        }

        Note noteToDelete = SelectedNote;
        await DeleteNoteAsync(noteToDelete);
    }

    private void NotesItemsView_ItemInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is Note note)
        {
            SelectNote(note);
        }
    }

    private async void NoteEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        await SaveSelectedNoteAsync();
    }

    private async Task SaveSelectedNoteAsync()
    {
        Note? note = SelectedNote;
        if (note is not null)
        {
            await SaveNoteAsync(note);
        }
    }

    private async Task SaveNoteAsync(Note note)
    {
        ClearError();

        if (_isSaving)
        {
            _pendingSaveNote = note;
            return;
        }

        await RunSaveQueueAsync(note);
    }

    private async Task RunSaveQueueAsync(Note note)
    {
        _isSaving = true;
        UpdateSaveState();

        try
        {
            Note? current = note;
            while (current is not null)
            {
                _pendingSaveNote = null;

                try
                {
                    await current.SaveAsync(ViewModel.Store);

                    if (ReferenceEquals(SelectedNote, current))
                    {
                        SaveStatusTextBlock.Text = "Saved";
                    }
                }
                catch (Exception exception)
                {
                    if (ReferenceEquals(SelectedNote, current))
                    {
                        SaveStatusTextBlock.Text = "Not saved";
                    }

                    ShowError(
                        "Note could not be saved",
                        $"{Describe(exception)} Check access to the vault folder, then select Retry.",
                        RetryOperation.SaveNote,
                        current);
                }

                current = _pendingSaveNote;
            }
        }
        finally
        {
            _isSaving = false;
            UpdateSaveState();
        }
    }

    private async Task DeleteNoteAsync(Note note)
    {
        ClearError();

        try
        {
            await note.DeleteAsync(ViewModel.Store);
        }
        catch (Exception exception)
        {
            ShowError(
                "Note could not be deleted",
                $"{Describe(exception)} Check access to the vault folder, then select Retry.",
                RetryOperation.DeleteNote,
                note);
            return;
        }

        if (ReferenceEquals(_pendingSaveNote, note))
        {
            _pendingSaveNote = null;
        }

        ViewModel.Remove(note);
        ApplySearch();

        if (ReferenceEquals(SelectedNote, note))
        {
            ClearSelection();
        }
    }

    private async void ErrorRetryButton_Click(object sender, RoutedEventArgs e)
    {
        RetryOperation operation = _retryOperation;
        Note? note = _retryNote;
        ResetErrorState();
        ErrorInfoBar.IsOpen = false;

        switch (operation)
        {
            case RetryOperation.LoadNotes:
                _loadFailed = false;
                VaultService.Current.Reset();
                await EnsureNotesLoadedAsync();
                ApplySearch();
                break;
            case RetryOperation.SaveNote:
                if (note is not null)
                {
                    await SaveNoteAsync(note);
                }

                break;
            case RetryOperation.DeleteNote:
                if (note is not null)
                {
                    await DeleteNoteAsync(note);
                }

                break;
            default:
                break;
        }
    }

    private void ErrorInfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (!ErrorInfoBar.IsOpen)
        {
            ResetErrorState();
        }
    }

    private void ShowError(
        string title,
        string message,
        RetryOperation operation,
        Note? note,
        InfoBarSeverity severity = InfoBarSeverity.Error)
    {
        ErrorInfoBar.Title = title;
        ErrorInfoBar.Message = message;
        ErrorInfoBar.Severity = severity;
        _retryOperation = operation;
        _retryNote = note;
        ErrorRetryButton.Visibility = operation == RetryOperation.None
            ? Visibility.Collapsed
            : Visibility.Visible;
        ErrorInfoBar.IsOpen = true;
    }

    private void ClearError()
    {
        ResetErrorState();

        if (ErrorInfoBar.IsOpen)
        {
            ErrorInfoBar.IsOpen = false;
        }
    }

    private void ResetErrorState()
    {
        _retryOperation = RetryOperation.None;
        _retryNote = null;
    }

    private static string Describe(Exception exception)
    {
        return string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;
    }

    private void SelectNote(Note note)
    {
        SelectedNote = note;
        NoteEditor.IsEnabled = true;
        NoteDateTextBlock.Text = note.Date.ToString("g");
        DeleteNoteButton.IsEnabled = VaultService.Current.IsWritable;
        SaveStatusTextBlock.Text = string.Empty;
        Bindings.Update();
        ApplyLayout(ActualWidth < NarrowWidthThreshold);
    }

    private void ClearSelection()
    {
        SelectedNote = null;
        NoteEditor.IsEnabled = false;
        NoteEditor.Text = string.Empty;
        NoteDateTextBlock.Text = "Select a note";
        DeleteNoteButton.IsEnabled = false;
        SaveStatusTextBlock.Text = string.Empty;
        Bindings.Update();
        ApplyLayout(ActualWidth < NarrowWidthThreshold);
    }

    private void BackToListButton_Click(object sender, RoutedEventArgs e)
    {
        ClearSelection();
    }

    private void UpdateListState()
    {
        bool isLoading = _isLoadingNotes || ViewModel.IsLoading;
        NotesLoadingRing.IsActive = isLoading;
        NotesLoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        NotesItemsView.Visibility = isLoading || ViewModel.IsEmpty
            ? Visibility.Collapsed
            : Visibility.Visible;
        EmptyStateTextBlock.Visibility = isLoading || !ViewModel.IsEmpty
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (!isLoading)
        {
            bool isSearching = !string.IsNullOrWhiteSpace(NoteSearchState.Query);
            EmptyStateTextBlock.Text = isSearching
                ? "No notes match your search. Clear the search box to see all notes."
                : "No notes yet. Select New note to create one.";
        }
    }

    private void UpdateSaveState()
    {
        SaveProgressRing.IsActive = _isSaving;
        SaveProgressRing.Visibility = _isSaving ? Visibility.Visible : Visibility.Collapsed;

        if (_isSaving)
        {
            SaveStatusTextBlock.Text = "Saving...";
        }
    }

    private void ApplyLayout(bool isNarrow)
    {
        bool hasSelection = SelectedNote is not null;
        NotesListPanel.Visibility = isNarrow && hasSelection ? Visibility.Collapsed : Visibility.Visible;
        NoteDetailPanel.Visibility = !isNarrow || hasSelection ? Visibility.Visible : Visibility.Collapsed;
        BackToListButton.Visibility = isNarrow && hasSelection ? Visibility.Visible : Visibility.Collapsed;
    }
}
