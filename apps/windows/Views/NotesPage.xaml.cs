using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NopeNote.Core.Notes;
using NopeNote.Core.Vault;
using NopeNote.Windows.Helpers;
using NopeNote.Windows.Models;
using NopeNote.Windows.Services;
using NopeNote.Windows.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
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
    private bool _isUpdatingTagFilter;
    private bool _isUpdatingMetadata;
    private Guid? _pendingNoteId;
    private Note? _pendingSaveNote;
    private Note? _retryNote;
    private RetryOperation _retryOperation = RetryOperation.None;
    private string? _conflictPath;
    private readonly DispatcherTimer _autoSaveTimer = new();

    public NotesPage()
    {
        ViewModel = new NotesViewModel();
        InitializeComponent();
        NoteEditor.Header = Strings.Get("NotesEditorHeader");
        HistoryButton.Label = Strings.Get("NotesHistoryButtonLabel");
        TagsEditor.PlaceholderText = Strings.Get("NotesTagsPlaceholder");
        PinCheckBox.Content = Strings.Get("NotesPinLabel");
        VaultNoticeDismiss.Content = Strings.Get("VaultDiagnosticsDismiss");
        InitializeFeatures();
        _autoSaveTimer.Interval = TimeSpan.FromSeconds(1.2);
        _autoSaveTimer.Tick += AutoSaveTimer_Tick;
        Loaded += NotesPage_Loaded;
        SizeChanged += NotesPage_SizeChanged;
        Unloaded += NotesPage_Unloaded;
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
        VaultService.Current.CommitFailed -= OnGitCommitFailed;
        VaultService.Current.CommitFailed += OnGitCommitFailed;
        ApplyLayout(ActualWidth < NarrowWidthThreshold);
        await EnsureNotesLoadedAsync();
    }

    private async void NotesPage_Unloaded(object sender, RoutedEventArgs e)
    {
        VaultService.Current.CommitFailed -= OnGitCommitFailed;
        _autoSaveTimer.Stop();
        await PersistEditorTextAsync();
    }

    private void OnGitCommitFailed(string message)
    {
        DispatcherQueue.TryEnqueue(
            () => ShowError(
                Strings.Get("GitCommitFailedTitle"),
                Strings.Format("GitCommitFailedMessage", message),
                RetryOperation.None,
                null,
                InfoBarSeverity.Warning));
    }

    private void NoteEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        _autoSaveTimer.Stop();

        if (SelectedNote is null ||
            string.Equals(NoteEditor.Text, SelectedNote.Text, StringComparison.Ordinal))
        {
            return;
        }

        _autoSaveTimer.Start();
        RefreshPreviewIfVisible();
    }

    private async void AutoSaveTimer_Tick(object? sender, object e)
    {
        _autoSaveTimer.Stop();
        await PersistEditorTextAsync();
    }

    private async Task PersistEditorTextAsync()
    {
        Note? note = SelectedNote;
        if (note is null)
        {
            return;
        }

        if (!string.Equals(NoteEditor.Text, note.Text, StringComparison.Ordinal))
        {
            note.Text = NoteEditor.Text;
        }

        await SaveNoteAsync(note);
    }

    private void UpdateMetadataBar(Note? note)
    {
        if (note is null)
        {
            NoteMetaBar.Visibility = Visibility.Collapsed;
            return;
        }

        _isUpdatingMetadata = true;
        TagsEditor.Text = string.Join(", ", note.Tags);
        RefreshFolderEditor(note.Folder);
        PinCheckBox.IsChecked = note.IsPinned;
        NoteMetaBar.Visibility = Visibility.Visible;
        _isUpdatingMetadata = false;
    }

    private async void TagsEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        await SaveNoteMetadataAsync();
    }

    private async void PinCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingMetadata)
        {
            return;
        }

        await SaveNoteMetadataAsync();
    }

    private async Task SaveNoteMetadataAsync()
    {
        Note? note = SelectedNote;
        IVaultStore? store = VaultService.Current.Store;
        if (note is null || note.IsDraft || store is null || _isUpdatingMetadata)
        {
            return;
        }

        List<string> tags = ParseTags(TagsEditor.Text);
        bool pinned = PinCheckBox.IsChecked == true;
        string folder = NormalizeFolder(FolderEditor.Text);
        if (TagsEqual(note.Tags, tags) && pinned == note.IsPinned &&
            string.Equals(folder, note.Folder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // 先落盘正文，避免这次元数据更新把编辑器里没保存的内容覆盖掉。
        await PersistEditorTextAsync();

        try
        {
            NoteDocument updated = await store.UpdateNoteAsync(
                note.Id,
                tags: tags,
                pinned: pinned,
                folder: folder);
            note.ApplyRestoredDocument(updated);
            Bindings.Update();
            UpdateMetadataBar(note);
            MarkDataChanged();
            RefreshNotes();
            SaveStatusTextBlock.Text = Strings.Get("NotesStatusSaved");
        }
        catch (Exception exception)
        {
            UpdateMetadataBar(note);
            ShowError(
                Strings.Get("NotesMetadataFailedTitle"),
                $"{Describe(exception)} {Strings.Get("NotesLoadFailedHint")}",
                RetryOperation.None,
                null);
        }
    }

    private static List<string> ParseTags(string text)
    {
        var tags = new List<string>();
        foreach (string part in text.Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string tag = part.Trim();
            if (tag.Length == 0)
            {
                continue;
            }

            if (!tags.Exists(existing =>
                    string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase)))
            {
                tags.Add(tag);
            }
        }

        return tags;
    }

    private static bool TagsEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
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
            RefreshTagFilter();
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
            RefreshTagFilter();
            await ReportVaultDiagnosticsAsync();
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

    private async Task ReportVaultDiagnosticsAsync()
    {
        if (VaultService.Current.DiagnosticsReported)
        {
            return;
        }

        VaultService.Current.DiagnosticsReported = true;
        VaultValidationReport? report = await VaultService.Current.DiagnoseAsync();
        if (report is null || report.Issues.Count == 0)
        {
            return;
        }

        string sample = string.Join(
            "; ",
            report.Issues.Take(3).Select(issue => $"{issue.Code}: {issue.RelativePath}"));
        VaultNoticeTitle.Text = Strings.Get("VaultDiagnosticsTitle");
        VaultNoticeMessage.Text = Strings.Format(
            "VaultDiagnosticsMessage",
            report.Issues.Count,
            sample);
        VaultNoticeBar.Visibility = Visibility.Visible;
    }

    private void VaultNoticeDismiss_Click(object sender, RoutedEventArgs e)
    {
        VaultNoticeBar.Visibility = Visibility.Collapsed;
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
        RefreshTagFilter();
        RefreshFolderFilter();
        RefreshFolderChoices();
        RefreshBacklinks();
        TrySelectPendingNote();
    }

    private void RefreshTagFilter()
    {
        _isUpdatingTagFilter = true;

        string? selected = ViewModel.TagFilter;
        TagFilterComboBox.Items.Clear();
        TagFilterComboBox.Items.Add(CreateTagItem(Strings.Get("NotesAllTags"), null));

        foreach (string tag in ViewModel.GetAvailableTags())
        {
            TagFilterComboBox.Items.Add(CreateTagItem(tag, tag));
        }

        TagFilterComboBox.SelectedIndex = 0;
        if (selected is not null)
        {
            foreach (object entry in TagFilterComboBox.Items)
            {
                if (entry is ComboBoxItem item && string.Equals(item.Tag as string, selected, StringComparison.Ordinal))
                {
                    TagFilterComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        string? effective = (TagFilterComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        _isUpdatingTagFilter = false;

        if (!string.Equals(effective, ViewModel.TagFilter, StringComparison.Ordinal))
        {
            ViewModel.SetTagFilter(effective);
        }
    }

    private static ComboBoxItem CreateTagItem(string content, string? tag)
    {
        var item = new ComboBoxItem { Content = content };
        if (tag is not null)
        {
            item.Tag = tag;
        }

        return item;
    }

    private void TagFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingTagFilter)
        {
            return;
        }

        string? tag = (TagFilterComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        ViewModel.SetTagFilter(tag);
        UpdateListState();

        if (SelectedNote is not null && !IsNoteVisible(SelectedNote))
        {
            ClearSelection();
        }

        TrySelectPendingNote();
    }

    private bool IsNoteVisible(Note note)
    {
        foreach (Note visible in ViewModel.Notes)
        {
            if (ReferenceEquals(visible, note))
            {
                return true;
            }
        }

        return false;
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

        Note note = new Note
        {
            Folder = ViewModel.FolderFilter ?? string.Empty,
        };
        if (ViewModel.TagFilter is not null)
        {
            TagFilterComboBox.SelectedIndex = 0;
        }

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
                catch (VaultConflictException conflict)
                {
                    if (ReferenceEquals(SelectedNote, current))
                    {
                        SaveStatusTextBlock.Text = Strings.Get("NotesStatusNotSaved");
                    }

                    ShowConflict(conflict);
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

    private async void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        Note? note = SelectedNote;
        IVaultStore? store = VaultService.Current.Store;
        if (note is null || store is null || note.IsDraft)
        {
            return;
        }

        // 先把编辑器里待保存的内容落盘，历史列表才包含最新一版。
        await PersistEditorTextAsync();

        IReadOnlyList<VaultSnapshotInfo> snapshots;
        try
        {
            snapshots = await store.ListHistoryAsync(note.Id);
        }
        catch (Exception exception)
        {
            ShowError(
                Strings.Get("NotesHistoryFailedTitle"),
                $"{Describe(exception)} {Strings.Get("NotesLoadFailedHint")}",
                RetryOperation.None,
                null);
            return;
        }

        if (snapshots.Count == 0)
        {
            ContentDialog emptyDialog = new()
            {
                XamlRoot = XamlRoot,
                Title = Strings.Get("NotesHistoryTitle"),
                Content = Strings.Get("NotesHistoryEmpty"),
                CloseButtonText = Strings.Get("CommonCancel"),
            };
            await emptyDialog.ShowAsync();
            return;
        }

        var items = new List<NoteVersionItem>(snapshots.Count);
        for (int index = snapshots.Count - 1; index >= 0; index--)
        {
            items.Add(new NoteVersionItem(snapshots[index]));
        }

        var list = new ListView
        {
            DisplayMemberPath = "Label",
            ItemsSource = items,
            MaxHeight = 200,
            SelectionMode = ListViewSelectionMode.Single,
        };

        var preview = new TextBox
        {
            AcceptsReturn = true,
            Height = 160,
            IsReadOnly = true,
            Text = string.Empty,
            TextWrapping = TextWrapping.Wrap,
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(list);
        panel.Children.Add(preview);

        NoteDocument? selectedVersion = null;
        string? selectedName = null;

        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = Strings.Get("NotesHistoryTitle"),
            Content = panel,
            PrimaryButtonText = Strings.Get("NotesHistoryRestore"),
            CloseButtonText = Strings.Get("CommonCancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };

        list.SelectionChanged += async (_, _) =>
        {
            if (list.SelectedItem is not NoteVersionItem item)
            {
                selectedName = null;
                selectedVersion = null;
                dialog.IsPrimaryButtonEnabled = false;
                return;
            }

            selectedName = item.SnapshotName;
            selectedVersion = null;
            dialog.IsPrimaryButtonEnabled = false;
            preview.Text = string.Empty;

            try
            {
                NoteDocument? document = await store.ReadHistoryAsync(note.Id, item.SnapshotName);
                if (selectedName != item.SnapshotName || document is null)
                {
                    return;
                }

                selectedVersion = document;
                preview.Text = document.Body;
                dialog.IsPrimaryButtonEnabled = true;
            }
            catch (Exception exception)
            {
                preview.Text = Describe(exception);
            }
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || selectedVersion is null)
        {
            return;
        }

        try
        {
            NoteDocument updated = await store.UpdateNoteAsync(note.Id, body: selectedVersion.Body);
            note.ApplyRestoredDocument(updated);
            Bindings.Update();
            MarkDataChanged();
            SaveStatusTextBlock.Text = Strings.Get("NotesHistoryRestored");
        }
        catch (Exception exception)
        {
            ShowError(
                Strings.Get("NotesHistoryFailedTitle"),
                $"{Describe(exception)} {Strings.Get("NotesLoadFailedHint")}",
                RetryOperation.None,
                null);
        }
    }

    private async void ErrorRetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_conflictPath is not null)
        {
            if (!ConflictNotice.TryShowInExplorer(_conflictPath))
            {
                ErrorInfoBar.Message = Strings.Get("ConflictOpenFailed");
            }

            return;
        }

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
        InfoBarSeverity severity = InfoBarSeverity.Error,
        string? conflictPath = null)
    {
        ErrorInfoBar.Title = title;
        ErrorInfoBar.Message = message;
        ErrorInfoBar.Severity = severity;
        _retryOperation = operation;
        _retryNote = note;
        _conflictPath = conflictPath;
        ErrorRetryButton.Content = conflictPath is null
            ? Strings.Get("NotesRetryButton.Content")
            : Strings.Get("ConflictShowCopy");
        ErrorRetryButton.Visibility = conflictPath is not null || operation != RetryOperation.None
            ? Visibility.Visible
            : Visibility.Collapsed;
        ErrorInfoBar.IsOpen = true;
    }

    private void ShowConflict(VaultConflictException conflict)
    {
        string path = ConflictNotice.ResolvePath(conflict.ConflictPath, ViewModel.Store.RootPath);
        ShowError(
            Strings.Get("NotesConflictTitle"),
            Strings.Format("NotesConflictMessage", path),
            RetryOperation.None,
            null,
            InfoBarSeverity.Warning,
            path);
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
        _conflictPath = null;
    }

    private static string Describe(Exception exception)
    {
        return string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;
    }

    private void SelectNote(Note note)
    {
        _autoSaveTimer.Stop();
        _ = PersistEditorTextAsync();

        SelectedNote = note;
        NoteEditor.IsEnabled = true;
        NoteDateTextBlock.Text = note.Date.ToString("g");
        DeleteNoteButton.IsEnabled = VaultService.Current.IsWritable;
        HistoryButton.Visibility = Visibility.Visible;
        UpdateMetadataBar(note);
        OnNoteSelected(note);
        SaveStatusTextBlock.Text = string.Empty;
        Bindings.Update();
        ApplyLayout(ActualWidth < NarrowWidthThreshold);
    }

    private void ClearSelection()
    {
        _autoSaveTimer.Stop();
        _ = PersistEditorTextAsync();

        SelectedNote = null;
        NoteEditor.IsEnabled = false;
        NoteEditor.Text = string.Empty;
        NoteDateTextBlock.Text = Strings.Get("NotesSelectPlaceholder");
        DeleteNoteButton.IsEnabled = false;
        HistoryButton.Visibility = Visibility.Collapsed;
        UpdateMetadataBar(null);
        OnNoteSelected(null);
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
        _autoSaveTimer.Stop();

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

internal sealed class NoteVersionItem
{
    public NoteVersionItem(VaultSnapshotInfo snapshot)
    {
        SnapshotName = snapshot.Name;
        Label = snapshot.TakenAt.LocalDateTime.ToString("g");
    }

    public string SnapshotName { get; }

    public string Label { get; }
}
