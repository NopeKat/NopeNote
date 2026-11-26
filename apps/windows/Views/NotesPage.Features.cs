using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NopeNote.Core.Notes;
using NopeNote.Core.Vault;
using NopeNote.Windows.Helpers;
using NopeNote.Windows.Models;
using NopeNote.Windows.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;
using StorageFile = Windows.Storage.StorageFile;

namespace NopeNote.Windows.Views;

public sealed partial class NotesPage
{
    private static readonly Regex WikiLinkPattern = new(
        @"\[\[(?<target>[^\]]+)\]\]",
        RegexOptions.Compiled);

    private bool _batchMode;
    private bool _isUpdatingFolderFilter;
    private bool _isUpdatingFolderEditor;
    private bool _previewVisible;

    private void InitializeFeatures()
    {
        NoteEditor.Paste += NoteEditor_Paste;

        TagFilterComboBox.PlaceholderText = Strings.Get("NotesAllTags");
        FolderFilterComboBox.PlaceholderText = Strings.Get("NotesAllFolders");
        TagFilterComboBox.Header = Strings.Get("NotesTagFilterHeader");
        FolderFilterComboBox.Header = Strings.Get("NotesFolderFilterHeader");
        FolderEditor.PlaceholderText = Strings.Get("NotesFolderPlaceholder");
        FolderEditor.Header = Strings.Get("NotesFolderFilterHeader");
        PreviewToggle.Label = Strings.Get("NotesPreviewButton");
        ShareButton.Label = Strings.Get("NotesShareButton");
        OpenWindowButton.Label = Strings.Get("NotesOpenWindowButton");
        BatchButton.Label = Strings.Get("NotesBatchButton");
        DailyNoteButton.Label = Strings.Get("NotesDailyButton");
        TemplateButton.Label = Strings.Get("NotesTemplateButton");
        BatchTagButton.Content = Strings.Get("NotesBatchAddTag");
        BatchMoveButton.Content = Strings.Get("NotesBatchMove");
        BatchDeleteButton.Content = Strings.Get("NotesBatchDelete");
        BatchDoneButton.Content = Strings.Get("NotesBatchDone");
        ExportMarkdownItem.Text = Strings.Get("NotesExportMarkdownMenu");
        ExportHtmlItem.Text = Strings.Get("NotesExportHtmlMenu");
        CopyNoteItem.Text = Strings.Get("NotesCopyMenu");

        LoadTemplateMenu();
    }

    // ---------- 文件夹 ----------

    private void RefreshFolderFilter()
    {
        _isUpdatingFolderFilter = true;

        string? selected = ViewModel.FolderFilter;
        FolderFilterComboBox.Items.Clear();
        FolderFilterComboBox.Items.Add(CreateFolderItem(Strings.Get("NotesAllFolders"), null));

        foreach (string folder in ViewModel.GetAvailableFolders())
        {
            FolderFilterComboBox.Items.Add(CreateFolderItem(folder, folder));
        }

        FolderFilterComboBox.SelectedIndex = 0;
        if (selected is not null)
        {
            foreach (object entry in FolderFilterComboBox.Items)
            {
                if (entry is ComboBoxItem item &&
                    string.Equals(item.Tag as string, selected, StringComparison.OrdinalIgnoreCase))
                {
                    FolderFilterComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        string? effective = (FolderFilterComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        _isUpdatingFolderFilter = false;

        if (!string.Equals(effective, ViewModel.FolderFilter, StringComparison.OrdinalIgnoreCase))
        {
            ViewModel.SetFolderFilter(effective);
        }
    }

    private void RefreshFolderChoices()
    {
        if (_isUpdatingMetadata)
        {
            return;
        }

        _isUpdatingFolderEditor = true;
        string current = SelectedNote?.Folder ?? string.Empty;
        FolderEditor.Items.Clear();
        foreach (string folder in ViewModel.GetAvailableFolders())
        {
            FolderEditor.Items.Add(folder);
        }

        FolderEditor.Text = current;
        _isUpdatingFolderEditor = false;
    }

    private void RefreshFolderEditor(string folder)
    {
        _isUpdatingFolderEditor = true;
        FolderEditor.Items.Clear();
        foreach (string existing in ViewModel.GetAvailableFolders())
        {
            FolderEditor.Items.Add(existing);
        }

        FolderEditor.Text = folder;
        _isUpdatingFolderEditor = false;
    }

    private void FolderFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingFolderFilter)
        {
            return;
        }

        string? folder = (FolderFilterComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        ViewModel.SetFolderFilter(folder);
        UpdateListState();
        TrySelectPendingNote();
    }

    private async void FolderEditor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingFolderEditor || _isUpdatingMetadata)
        {
            return;
        }

        string? folder = FolderEditor.SelectedItem as string;
        if (folder is not null && !string.Equals(folder, SelectedNote?.Folder, StringComparison.OrdinalIgnoreCase))
        {
            await SaveNoteMetadataAsync();
        }
    }

    private async void FolderEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingFolderEditor || _isUpdatingMetadata)
        {
            return;
        }

        await SaveNoteMetadataAsync();
    }

    private static string NormalizeFolder(string? folder)
    {
        string value = (folder ?? string.Empty).Replace('\\', '/').Trim().Trim('/');
        while (value.Contains("//", StringComparison.Ordinal))
        {
            value = value.Replace("//", "/", StringComparison.Ordinal);
        }

        return value;
    }

    private static ComboBoxItem CreateFolderItem(string text, string? tag)
        => new() { Content = text, Tag = tag };

    // ---------- 批量操作 ----------

    private void BatchButton_Click(object sender, RoutedEventArgs e)
        => SetBatchMode(!_batchMode);

    private void BatchCancelButton_Click(object sender, RoutedEventArgs e)
        => SetBatchMode(false);

    private void SetBatchMode(bool enabled)
    {
        _batchMode = enabled;
        BatchBar.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        BatchButton.Label = enabled ? Strings.Get("NotesBatchDone") : Strings.Get("NotesBatchButton");

        foreach (Note note in ViewModel.AllNotes)
        {
            if (enabled)
            {
                note.PropertyChanged += Note_SelectionChanged;
            }
            else
            {
                note.PropertyChanged -= Note_SelectionChanged;
                note.IsSelected = false;
            }

            note.SelectionVisibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        }

        UpdateBatchCount();
    }

    private void Note_SelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Note.IsSelected))
        {
            UpdateBatchCount();
        }
    }

    private void UpdateBatchCount()
    {
        int count = ViewModel.AllNotes.Count(note => note.IsSelected);
        BatchCountTextBlock.Text = Strings.Format("NotesBatchCount", count);
        BatchTagButton.IsEnabled = count > 0;
        BatchMoveButton.IsEnabled = count > 0;
        BatchDeleteButton.IsEnabled = count > 0;
    }

    private IReadOnlyList<Note> SelectedNotes()
        => ViewModel.AllNotes.Where(note => note.IsSelected).ToList();

    private async Task<bool> SaveSelectionBeforeBatchAsync(IReadOnlyList<Note> notes)
    {
        Note? selected = SelectedNote;
        if (selected is null ||
            !notes.Any(note => ReferenceEquals(note, selected)))
        {
            return true;
        }

        await SaveSelectedNoteAsync();
        return !selected.HasUnsavedChanges;
    }

    private async void BatchTagButton_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<Note> notes = SelectedNotes();
        if (notes.Count == 0)
        {
            return;
        }

        if (!await SaveSelectionBeforeBatchAsync(notes))
        {
            return;
        }

        TextBox input = new() { PlaceholderText = Strings.Get("NotesTagsPlaceholder") };
        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = Strings.Get("NotesBatchTagTitle"),
            Content = input,
            PrimaryButtonText = Strings.Get("NotesBatchApply"),
            CloseButtonText = Strings.Get("CommonCancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        List<string> added = ParseTags(input.Text);
        if (added.Count == 0)
        {
            return;
        }

        await RunBatchUpdateAsync(notes, (note, store) =>
            store.UpdateNoteAsync(note.Id, tags: MergeTags(note, added), folder: note.Folder));
    }

    private async void BatchFolderButton_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<Note> notes = SelectedNotes();
        if (notes.Count == 0)
        {
            return;
        }

        if (!await SaveSelectionBeforeBatchAsync(notes))
        {
            return;
        }

        TextBox input = new()
        {
            PlaceholderText = Strings.Get("NotesFolderPlaceholder"),
            Text = notes[0].Folder,
        };        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = Strings.Get("NotesBatchFolderTitle"),
            Content = input,
            PrimaryButtonText = Strings.Get("NotesBatchApply"),
            CloseButtonText = Strings.Get("CommonCancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        string folder = NormalizeFolder(input.Text);
        await RunBatchUpdateAsync(notes, (note, store) =>
            store.UpdateNoteAsync(note.Id, tags: note.Tags, pinned: note.IsPinned, folder: folder));
    }

    private async Task RunBatchUpdateAsync(
        IReadOnlyList<Note> notes,
        Func<Note, IVaultStore, Task<NoteDocument>> update)
    {
        IVaultStore? store = VaultService.Current.Store;
        if (store is null)
        {
            return;
        }

        for (int index = 0; index < notes.Count; index++)
        {
            try
            {
                NoteDocument document = await update(notes[index], store);
                notes[index].ApplyRestoredDocument(document);
                notes[index].IsSelected = false;
            }
            catch (Exception exception)
            {
                ShowError(
                    Strings.Get("NotesMetadataFailedTitle"),
                    $"{Describe(exception)} {Strings.Get("NotesLoadFailedHint")}",
                    RetryOperation.None,
                    null);
                break;
            }
        }

        MarkDataChanged();
        RefreshNotes();
        UpdateBatchCount();
    }

    private async void BatchDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<Note> notes = SelectedNotes();
        if (notes.Count == 0)
        {
            return;
        }

        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = Strings.Get("NotesBatchDeleteTitle"),
            Content = Strings.Format("NotesBatchDeleteBody", notes.Count),
            PrimaryButtonText = Strings.Get("NotesDeleteDialogPrimary"),
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

        bool deletingSelected = SelectedNote is not null &&
            notes.Any(note => ReferenceEquals(note, SelectedNote));
        if (deletingSelected && !await SaveSelectionBeforeBatchAsync(notes))
        {
            return;
        }

        foreach (Note note in notes)
        {
            try
            {
                await note.DeleteAsync(store);
                ViewModel.Remove(note);
            }
            catch (Exception exception)
            {
                ShowError(
                    Strings.Get("NotesDeleteFailedTitle"),
                    $"{Describe(exception)} {Strings.Get("NotesLoadFailedHint")}",
                    RetryOperation.None,
                    null);
                break;
            }
        }

        if (deletingSelected &&
            !ViewModel.AllNotes.Any(note => ReferenceEquals(note, SelectedNote)))
        {
            ClearSelection();
        }

        MarkDataChanged();
        RefreshNotes();
        UpdateBatchCount();
    }

    private static IReadOnlyList<string> MergeTags(Note note, IReadOnlyList<string> added)
    {
        List<string> merged = new(note.Tags);
        foreach (string tag in added)
        {
            if (!merged.Exists(existing =>
                    string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase)))
            {
                merged.Add(tag);
            }
        }

        return merged;
    }

    // ---------- 模板与每日笔记 ----------

    private void LoadTemplateMenu()
    {
        TemplateMenu.Items.Clear();

        string templatesPath = Path.Combine(VaultService.Current.VaultPath, ".templates");
        try
        {
            Directory.CreateDirectory(templatesPath);
        }
        catch (IOException)
        {
            return;
        }

        foreach (string path in Directory.EnumerateFiles(templatesPath, "*.md"))
        {
            MenuFlyoutItem item = new()
            {
                Text = Path.GetFileNameWithoutExtension(path),
                Tag = path,
            };
            item.Click += TemplateItem_Click;
            TemplateMenu.Items.Add(item);
        }

        if (TemplateMenu.Items.Count == 0)
        {
            TemplateMenu.Items.Add(new MenuFlyoutItem
            {
                Text = Strings.Get("NotesNoTemplates"),
                IsEnabled = false,
            });
        }

        MenuFlyoutItem saveItem = new() { Text = Strings.Get("NotesSaveAsTemplate") };
        saveItem.Click += SaveAsTemplateItem_Click;
        TemplateMenu.Items.Add(new MenuFlyoutSeparator());
        TemplateMenu.Items.Add(saveItem);
    }

    private async void TemplateItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not string path)
        {
            return;
        }

        try
        {
            string body = await File.ReadAllTextAsync(path);
            await CreateNoteFromBodyAsync(body, ViewModel.FolderFilter ?? string.Empty);
        }
        catch (Exception exception)
        {
            ShowError(
                Strings.Get("NotesTemplateFailedTitle"),
                Describe(exception),
                RetryOperation.None,
                null);
        }
    }

    private async void SaveAsTemplateItem_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNote is null)
        {
            return;
        }

        TextBox input = new()
        {
            Text = SelectedNote.DisplayTitle,
            PlaceholderText = Strings.Get("NotesTemplateNamePlaceholder"),
        };
        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = Strings.Get("NotesSaveTemplateTitle"),
            Content = input,
            PrimaryButtonText = Strings.Get("NotesBatchApply"),
            CloseButtonText = Strings.Get("CommonCancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        string name = SanitizeFileName(input.Text.Trim());
        if (name.Length == 0)
        {
            return;
        }

        string folder = Path.Combine(VaultService.Current.VaultPath, ".templates");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, name + ".md"), NoteEditor.Text);
        LoadTemplateMenu();
    }

    private async void DailyNoteButton_Click(object sender, RoutedEventArgs e)
    {
        string title = DateTime.Now.ToString("yyyy-MM-dd");
        Note? existing = ViewModel.AllNotes.FirstOrDefault(note =>
            string.Equals(note.DisplayTitle, title, StringComparison.Ordinal));
        if (existing is not null)
        {
            SelectNote(existing);
            return;
        }

        string body = $"# {title}\n\n";
        string dailyTemplate = Path.Combine(VaultService.Current.VaultPath, ".templates", "Daily.md");
        if (File.Exists(dailyTemplate))
        {
            body = (await File.ReadAllTextAsync(dailyTemplate)).Replace("{date}", title);
            if (!body.TrimStart().StartsWith($"# {title}", StringComparison.Ordinal))
            {
                body = $"# {title}\n\n{body}";
            }
        }

        await CreateNoteFromBodyAsync(body, ViewModel.FolderFilter ?? string.Empty);
    }

    private async Task CreateNoteFromBodyAsync(string body, string folder)
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

        Note note = new()
        {
            Text = body,
            Folder = NormalizeFolder(folder),
        };

        ViewModel.Add(note);
        RefreshNotes();
        SelectNote(note);
        NoteEditor.Focus(FocusState.Programmatic);
        await SaveNoteAsync(note);
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '-');
        }

        return name.Trim();
    }

    // ---------- Markdown 工具与预览 ----------

    private void BoldButton_Click(object sender, RoutedEventArgs e)
        => WrapSelection("**", "**", Strings.Get("NotesBoldPlaceholder"));

    private void ItalicButton_Click(object sender, RoutedEventArgs e)
        => WrapSelection("*", "*", Strings.Get("NotesItalicPlaceholder"));

    private void CodeButton_Click(object sender, RoutedEventArgs e)
        => WrapSelection("`", "`", Strings.Get("NotesCodePlaceholder"));

    private void HeadingButton_Click(object sender, RoutedEventArgs e)
        => InsertLinePrefix("## ");

    private void BulletButton_Click(object sender, RoutedEventArgs e)
        => InsertLinePrefix("- ");

    private void ChecklistButton_Click(object sender, RoutedEventArgs e)
        => InsertLinePrefix("- [ ] ");

    private void TableButton_Click(object sender, RoutedEventArgs e)
    {
        InsertAtCaret(
            "\n| Column | Column |\n| --- | --- |\n| Value | Value |\n");
    }

    private async void LinkButton_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<Note> candidates = ViewModel.AllNotes
            .Where(note => !note.IsDraft && !ReferenceEquals(note, SelectedNote))
            .ToList();

        ListView list = new()
        {
            DisplayMemberPath = nameof(Note.DisplayTitle),
            ItemsSource = candidates,
            MinWidth = 280,
            MaxHeight = 320,
            SelectionMode = ListViewSelectionMode.Single,
        };

        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = Strings.Get("NotesLinkNoteTitle"),
            Content = list,
            PrimaryButtonText = Strings.Get("NotesBatchApply"),
            IsPrimaryButtonEnabled = candidates.Count > 0,
            CloseButtonText = Strings.Get("CommonCancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary &&
            list.SelectedItem is Note target)
        {
            InsertAtCaret($"[[{target.DisplayTitle}]]");
        }
    }

    private void WrapSelection(string prefix, string suffix, string placeholder)
    {
        int start = NoteEditor.SelectionStart;
        int length = NoteEditor.SelectionLength;
        string selected = length > 0 ? NoteEditor.SelectedText : placeholder;
        NoteEditor.SelectedText = prefix + selected + suffix;
        NoteEditor.SelectionStart = start + prefix.Length;
        NoteEditor.SelectionLength = selected.Length;
        NoteEditor.Focus(FocusState.Programmatic);
    }

    private void InsertLinePrefix(string prefix)
    {
        int start = NoteEditor.SelectionStart;
        int lineStart = start > 0
            ? NoteEditor.Text.LastIndexOf('\n', start - 1) + 1
            : 0;
        NoteEditor.SelectionStart = lineStart;
        NoteEditor.SelectionLength = 0;
        NoteEditor.SelectedText = prefix;
        NoteEditor.SelectionStart = start + prefix.Length;
        NoteEditor.Focus(FocusState.Programmatic);
    }

    private void InsertAtCaret(string text)
    {
        int start = NoteEditor.SelectionStart;
        NoteEditor.SelectedText = text;
        NoteEditor.SelectionStart = start + text.Length;
        NoteEditor.SelectionLength = 0;
        NoteEditor.Focus(FocusState.Programmatic);
    }

    private void PreviewToggle_Click(object sender, RoutedEventArgs e)
    {
        _previewVisible = PreviewToggle.IsChecked == true;
        UpdatePreviewMode();
    }

    private void UpdatePreviewMode()
    {
        NoteEditorCard.Visibility = _previewVisible ? Visibility.Collapsed : Visibility.Visible;
        PreviewCard.Visibility = _previewVisible ? Visibility.Visible : Visibility.Collapsed;

        if (_previewVisible)
        {
            RefreshPreview();
        }
    }

    private void RefreshPreviewIfVisible()
    {
        if (_previewVisible)
        {
            RefreshPreview();
        }
    }

    private void RefreshPreview()
    {
        PreviewPanel.Children.Clear();
        if (SelectedNote is null)
        {
            return;
        }

        PreviewPanel.Children.Add(MarkdownRenderer.Render(NoteEditor.Text, VaultService.Current.VaultPath));
    }

    // ---------- 附件 ----------

    private async void NoteEditor_Paste(object sender, TextControlPasteEventArgs e)
    {
        IVaultStore? store = VaultService.Current.Store;
        if (SelectedNote is null || store is null || !store.Capabilities.Writable)
        {
            return;
        }

        DataPackageView content = Clipboard.GetContent();
        if (!content.Contains(StandardDataFormats.Bitmap))
        {
            return;
        }

        e.Handled = true;
        try
        {
            string? path = await AttachmentStore.SaveClipboardImageAsync(store);
            if (path is not null)
            {
                InsertAtCaret($"\n![pasted image]({path})\n");
            }
        }
        catch (Exception exception)
        {
            ShowError(
                Strings.Get("NotesAttachmentFailedTitle"),
                Describe(exception),
                RetryOperation.None,
                null);
        }
    }

    private void NoteEditor_DragOver(object sender, DragEventArgs e)
    {
        if (SelectedNote is not null &&
            VaultService.Current.IsWritable &&
            e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = Strings.Get("NotesAttachmentDropCaption");
            e.DragUIOverride.IsCaptionVisible = true;
        }
    }

    private async void NoteEditor_Drop(object sender, DragEventArgs e)
    {
        IVaultStore? store = VaultService.Current.Store;
        if (SelectedNote is null ||
            store is null ||
            !store.Capabilities.Writable ||
            !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        foreach (var item in items.OfType<StorageFile>())
        {
            try
            {
                string path = await AttachmentStore.SaveFileAsync(store, item);
                string name = Path.GetFileNameWithoutExtension(item.Name);
                bool isImage = IsImageExtension(Path.GetExtension(item.Name));
                InsertAtCaret(isImage ? $"![{name}]({path})\n" : $"[{item.Name}]({path})\n");
            }
            catch (Exception exception)
            {
                ShowError(
                    Strings.Get("NotesAttachmentFailedTitle"),
                    Describe(exception),
                    RetryOperation.None,
                    null);
                break;
            }
        }
    }

    private static bool IsImageExtension(string extension)
        => extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);

    // ---------- 单篇导出、复制与独立窗口 ----------

    private async void ExportMarkdownButton_Click(object sender, RoutedEventArgs e)
        => await ExportSelectedNoteAsync(".md", "NotesExportMarkdownType", markdown: true);

    private async void ExportHtmlButton_Click(object sender, RoutedEventArgs e)
        => await ExportSelectedNoteAsync(".html", "NotesExportHtmlType", markdown: false);

    private async Task ExportSelectedNoteAsync(string extension, string typeKey, bool markdown)
    {
        Note? note = SelectedNote;
        if (note is null)
        {
            return;
        }

        nint hwnd = WindowHelper.GetWindowHandle(WindowHelper.ActiveWindows.FirstOrDefault());
        if (hwnd == 0)
        {
            return;
        }

        FileSavePicker picker = new()
        {
            SuggestedFileName = SanitizeFileName(note.DisplayTitle),
        };
        picker.FileTypeChoices.Add(Strings.Get(typeKey), new List<string> { extension });
        InitializeWithWindow.Initialize(picker, hwnd);

        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        string content = markdown
            ? $"# {note.DisplayTitle}\n\n{NoteEditor.Text}"
            : BuildHtml(note.DisplayTitle, NoteEditor.Text);
        await File.WriteAllTextAsync(file.Path, content, new UTF8Encoding(false));
        SaveStatusTextBlock.Text = Strings.Get("NotesExportDone");
    }

    private static string BuildHtml(string title, string body)
    {
        string escaped = System.Net.WebUtility.HtmlEncode(body);
        string heading = System.Net.WebUtility.HtmlEncode(title);
        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>" +
               heading +
               "</title><style>body{font-family:Segoe UI,sans-serif;max-width:760px;margin:40px auto;line-height:1.6;color:#222}h1{font-size:22px}pre{white-space:pre-wrap;background:#f5f5f5;padding:16px;border-radius:6px}</style></head><body><h1>" +
               heading +
               "</h1><pre>" +
               escaped +
               "</pre></body></html>";
    }

    private void CopyNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNote is null)
        {
            return;
        }

        DataPackage package = new();
        package.SetText(NoteEditor.Text);
        Clipboard.SetContent(package);
        SaveStatusTextBlock.Text = Strings.Get("NotesCopyDone");
    }

    private async void OpenWindowButton_Click(object sender, RoutedEventArgs e)
    {
        Note? note = SelectedNote;
        if (note is null)
        {
            return;
        }

        await PersistEditorTextAsync();
        if (note.IsDraft)
        {
            return;
        }

        NoteWindow window = new(note.Id);
        WindowHelper.TrackWindow(window);
        window.Activate();
    }

    // ---------- 反向链接 ----------

    private void OnNoteSelected(Note? note)
    {
        _previewVisible = false;
        PreviewToggle.IsChecked = false;
        UpdatePreviewMode();

        bool visible = note is not null;
        ShareButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        OpenWindowButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        PreviewToggle.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        RefreshBacklinks();
    }

    private void RefreshBacklinks()
    {
        Note? note = SelectedNote;
        BacklinksList.ItemsSource = null;

        if (note is null)
        {
            BacklinksExpander.Visibility = Visibility.Collapsed;
            return;
        }

        var references = new List<Note>();
        foreach (Note other in ViewModel.AllNotes)
        {
            if (!ReferenceEquals(other, note) && LinksTo(other.Text, note.DisplayTitle))
            {
                references.Add(other);
            }
        }

        BacklinksList.ItemsSource = references;
        BacklinksHeader.Text = Strings.Format("NotesBacklinksHeader", references.Count);
        BacklinksExpander.Visibility = references.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BacklinksExpander.IsExpanded = false;
    }

    private static bool LinksTo(string text, string title)
    {
        foreach (Match match in WikiLinkPattern.Matches(text))
        {
            string target = match.Groups["target"].Value.Trim();
            string lastSegment = target[(target.LastIndexOf('/') + 1)..];
            if (string.Equals(lastSegment, title, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(target, title, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void BacklinksList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Note note)
        {
            SelectNote(note);
        }
    }
}
