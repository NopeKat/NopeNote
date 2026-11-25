using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
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
    private bool _loadFailed;
    private int _loadedVersion = -1;
    private Guid? _pendingNoteId;
    private Note? _pendingSaveNote;
    private Note? _retryNote;
    private RetryOperation _retryOperation = RetryOperation.None;

    public NotesPage()
    {
        ViewModel = new NotesViewModel();
        InitializeComponent();
        NoteEditor.Header = Strings.Get("NotesEditorHeader");
        Loaded += NotesPage_Loaded;
        SizeChanged += NotesPage_SizeChanged;
    }

    public NotesViewModel ViewModel { get; }

    public Note? SelectedNote { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _pendingNoteId = e.Parameter is Guid noteId ? noteId : null;
        TrySelectPendingNote();
    }

    private static double NarrowWidthThreshold =>
        (double)Microsoft.UI.Xaml.Application.Current.Resources["MasterDetailBreakpointWidth"];

    private void NotesPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyLayout(e.NewSize.Width < NarrowWidthThreshold);
    }

    private async void NotesPage_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLayout(ActualWidth < NarrowWidthThreshold);
        await EnsureNotesLoadedAsync();
    }

    private async Task EnsureNotesLoadedAsync()
    {
        if (_loadFailed || _isLoadingNotes)
        {
            return;
        }

        // 页面缓存会复用同一个实例：仓库和版本都没变就不必再重建列表。
        if (ViewModel.IsLoaded && ReferenceEquals(ViewModel.StoreOrNull, VaultService.Current.Store))
        {
            if (_loadedVersion == VaultService.Current.DataVersion)
            {
                return;
            }

            await ViewModel.LoadAsync();
            _loadedVersion = VaultService.Current.DataVersion;
            UpdateListState();
            TrySelectPendingNote();
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
                    Strings.Get("NotesVaultOpenFailedTitle"),
                    $"{opened.Message} {Strings.Get("NotesVaultOpenFailedHint")}",
                    RetryOperation.LoadNotes,
                    null);
                return;
            }

            ViewModel.Attach(opened.Store);
            await ViewModel.LoadAsync();
            _loadedVersion = VaultService.Current.DataVersion;
            ApplyVaultState(opened);
        }
        catch (Exception exception)
        {
            _loadFailed = true;
            ShowError(
                Strings.Get("NotesLoadFailedTitle"),
                $"{Describe(exception)} {Strings.Get("NotesLoadFailedHint")}",
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
                Strings.Get("NotesVaultReadOnlyTitle"),
                opened.Message,
                RetryOperation.None,
                null,
                InfoBarSeverity.Warning);
        }
    }

    private void RefreshNotes()
    {
        ViewModel.ApplyFilter(string.Empty);
        UpdateListState();
        TrySelectPendingNote();
    }

    // 本页自己已经更新了列表，同时把版本号对齐，避免下次进入再白刷一遍。
    private void MarkDataChanged()
    {
        VaultService.Current.NotifyDataChanged();
        _loadedVersion = VaultService.Current.DataVersion;
    }

    private void TrySelectPendingNote()
    {
        if (_pendingNoteId is not Guid noteId || !ViewModel.IsLoaded)
        {
            return;
        }

        _pendingNoteId = null;
        foreach (Note note in ViewModel.Notes)
        {
            if (note.Id == noteId)
            {
                SelectNote(note);
                return;
            }
        }
    }

    private async void NewNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!VaultService.Current.IsWritable)
        {
            ShowError(
                Strings.Get("NotesVaultReadOnlyCreateTitle"),
                Strings.Get("NotesVaultReadOnlyCreateMessage"),
                RetryOperation.None,
                null,
                InfoBarSeverity.Warning);
            return;
        }

        Note note = new Note();
        ViewModel.Add(note);
        RefreshNotes();
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
            Title = Strings.Get("NotesDeleteDialogTitle"),
            Content = Strings.Get("NotesDeleteDialogBody"),
            PrimaryButtonText = Strings.Get("NotesDeleteDialogPrimary"),
            CloseButtonText = Strings.Get("CommonCancel"),
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
                    bool hadUnsavedChanges = current.HasUnsavedChanges;
                    await current.SaveAsync(ViewModel.Store);

                    if (hadUnsavedChanges)
                    {
                        MarkDataChanged();
                    }

                    if (ReferenceEquals(SelectedNote, current))
                    {
                        SaveStatusTextBlock.Text = Strings.Get("NotesStatusSaved");
                    }
                }
                catch (Exception exception)
                {
                    if (ReferenceEquals(SelectedNote, current))
                    {
                        SaveStatusTextBlock.Text = Strings.Get("NotesStatusNotSaved");
                    }

                    ShowError(
                        Strings.Get("NotesSaveFailedTitle"),
                        $"{Describe(exception)} {Strings.Get("NotesLoadFailedHint")}",
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
                Strings.Get("NotesDeleteFailedTitle"),
                $"{Describe(exception)} {Strings.Get("NotesLoadFailedHint")}",
                RetryOperation.DeleteNote,
                note);
            return;
        }

        if (ReferenceEquals(_pendingSaveNote, note))
        {
            _pendingSaveNote = null;
        }

        ViewModel.Remove(note);
        MarkDataChanged();
        RefreshNotes();

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
        NoteDateTextBlock.Text = Strings.Get("NotesSelectPlaceholder");
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
            EmptyStateTextBlock.Text = Strings.Get("NotesEmptyDefault");
        }
    }

    internal bool HasUnsavedWork()
    {
        Note? note = SelectedNote;
        if (note is null)
        {
            return false;
        }

        // 文本框还没失焦时，输入还停留在控件里，也要算作未保存内容。
        return NoteEditor.Text != note.Text || note.HasUnsavedChanges;
    }

    internal async Task<bool> FlushBeforeCloseAsync()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            while (_isSaving)
            {
                await Task.Delay(50);
            }

            Note? note = SelectedNote;
            if (note is null)
            {
                return true;
            }

            if (NoteEditor.Text != note.Text)
            {
                note.Text = NoteEditor.Text;
            }

            if (!note.HasUnsavedChanges)
            {
                return true;
            }

            await SaveNoteAsync(note);
        }

        return !HasUnsavedWork();
    }

    private void UpdateSaveState()
    {
        SaveProgressRing.IsActive = _isSaving;
        SaveProgressRing.Visibility = _isSaving ? Visibility.Visible : Visibility.Collapsed;

        if (_isSaving)
        {
            SaveStatusTextBlock.Text = Strings.Get("NotesStatusSaving");
        }
    }

    private void ApplyLayout(bool isNarrow)
    {
        bool hasSelection = SelectedNote is not null;

        if (isNarrow)
        {
            ListColumn.Width = hasSelection ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            DetailColumn.Width = hasSelection ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            ListPane.Visibility = hasSelection ? Visibility.Collapsed : Visibility.Visible;
            DetailPane.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            ListColumn.Width = (GridLength)Application.Current.Resources["MasterDetailListPaneWidth"];
            DetailColumn.Width = new GridLength(1, GridUnitType.Star);
            ListPane.Visibility = Visibility.Visible;
            DetailPane.Visibility = Visibility.Visible;
        }

        BackToListButton.Visibility = isNarrow && hasSelection ? Visibility.Visible : Visibility.Collapsed;
    }
}
