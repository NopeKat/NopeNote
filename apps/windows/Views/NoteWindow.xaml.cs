using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NopeNote.Core.Notes;
using NopeNote.Core.Vault;
using NopeNote.Windows.Helpers;
using NopeNote.Windows.Services;
using System;
using System.Threading.Tasks;
using Windows.Graphics;

namespace NopeNote.Windows.Views;

/// <summary>A second window bound to one note, for side-by-side editing.</summary>
public sealed partial class NoteWindow : Window
{
    private readonly Guid _noteId;
    private readonly AppServices _services;
    private readonly DispatcherTimer _autoSaveTimer = new();
    private NoteDocument? _document;
    private string _savedText = string.Empty;
    private bool _saving;
    private bool _sizing;
    private bool _writable;
    private bool _closing;
    private bool _promptingClose;
    private string? _conflictPath;

    public NoteWindow(Guid noteId, AppServices services)
    {
        _noteId = noteId;
        _services = services;
        InitializeComponent();

        Title = Strings.Get("NoteWindowTitle");
        SaveButton.Content = Strings.Get("NoteWindowSave");
        CloseButton.Content = Strings.Get("CommonClose");
        OpenCopyButton.Content = Strings.Get("ConflictShowCopy");
        _autoSaveTimer.Interval = TimeSpan.FromSeconds(1.2);
        _autoSaveTimer.Tick += AutoSaveTimer_Tick;
        AppWindow.SetIcon("Assets/Tiles/NopeNoteIcon.ico");
        AppWindow.Closing += AppWindow_Closing;
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        BackdropHelper.Apply(this);

        if (!_sizing)
        {
            double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
            AppWindow.Resize(new SizeInt32((int)(760 * scale), (int)(620 * scale)));
            _sizing = true;
        }

        await LoadAsync();
        Editor.Focus(FocusState.Programmatic);
    }

    private async Task LoadAsync()
    {
        try
        {
            VaultOpenResult opened = await _services.Vault.GetVaultAsync();
            if (opened.Store is null)
            {
                ShowError(VaultProblemText.Describe(opened));
                return;
            }

            NoteDocument? document = await opened.Store.ReadNoteAsync(_noteId);
            if (document is null)
            {
                ShowError(Strings.Get("NoteWindowMissing"));
                return;
            }

            _document = document;
            Editor.Text = document.Body;
            _savedText = document.Body;
            _writable = opened.Store.Capabilities.Writable;
            Editor.IsReadOnly = !_writable;
            SaveButton.IsEnabled = _writable;
            TitleTextBlock.Text = NoteTitle.Display(document.Summary.Title);
            Editor.IsEnabled = true;
        }
        catch (Exception exception)
        {
            ShowError(Describe(exception));
        }
    }

    private static string Describe(Exception exception)
        => string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_document is null)
        {
            return;
        }

        StatusTextBlock.Text = string.Empty;
        UpdateBodySizeStatus();
        _autoSaveTimer.Stop();
        if (!string.Equals(Editor.Text, _savedText, StringComparison.Ordinal))
        {
            _autoSaveTimer.Start();
        }
    }

    private void UpdateBodySizeStatus()
    {
        if (BodySize.IsOverNoticeThreshold(Editor.Text))
        {
            StatusTextBlock.Text = Strings.Get("NoteBodyTooLarge");
        }
    }

    private async void AutoSaveTimer_Tick(object? sender, object e)
    {
        _autoSaveTimer.Stop();
        await SaveAsync();
    }

    private void SaveAccelerator_Invoked(
        KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = SaveAsync();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        await SaveAsync();
    }

    private async Task<bool> SaveAsync()
    {
        if (_document is null || _saving || !_writable)
        {
            return false;
        }

        string text = Editor.Text;
        if (string.Equals(text, _savedText, StringComparison.Ordinal))
        {
            StatusTextBlock.Text = Strings.Get("NoteWindowSaved");
            return true;
        }

        IVaultStore? store = _services.Vault.Store;
        if (store is null)
        {
            ShowError(Strings.Get("NoteWindowMissing"));
            return false;
        }

        _saving = true;
        try
        {
            NoteDocument updated = await store.UpdateNoteAsync(
                _noteId,
                body: text,
                expectedUpdated: _document.Summary.Updated,
                expectedHash: _document.ContentHash);
            _document = updated;
            _savedText = updated.Body;
            TitleTextBlock.Text = NoteTitle.Display(updated.Summary.Title);
            StatusTextBlock.Text = Strings.Get("NoteWindowSaved");
            UpdateBodySizeStatus();
            _services.Vault.NotifyDataChanged();
            return true;
        }
        catch (VaultConflictException conflict)
        {
            // 本地版本已写进冲突副本；把基准刷新到磁盘最新版，下一次保存不再
            // 重复撞同一个旧 revision。编辑器里的本地文本保持不变。
            try
            {
                NoteDocument? current = await store.ReadNoteAsync(_noteId);
                if (current is not null)
                {
                    _document = current;
                    _savedText = current.Body;
                }
            }
            catch (Exception exception)
            {
                FileLog.Warn($"conflict rebase failed: {exception.Message}");
            }

            ShowConflict(conflict.ConflictPath, store.RootPath);
            return false;
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            return false;
        }
        finally
        {
            _saving = false;
        }
    }

    private void ShowError(string message)
    {
        _conflictPath = null;
        OpenCopyButton.Visibility = Visibility.Collapsed;
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }

    private void ShowConflict(string conflictPath, string vaultRoot)
    {
        string fullPath = ConflictNotice.ResolvePath(conflictPath, vaultRoot);
        _conflictPath = fullPath;
        ErrorTextBlock.Text = Strings.Format("NoteWindowConflict", fullPath);
        ErrorTextBlock.Visibility = Visibility.Visible;
        OpenCopyButton.Visibility = Visibility.Visible;
    }

    private void OpenCopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_conflictPath is null)
        {
            return;
        }

        if (!ConflictNotice.TryShowInExplorer(_conflictPath))
        {
            ErrorTextBlock.Text = Strings.Get("ConflictOpenFailed");
        }
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (await ConfirmCloseAsync())
        {
            _closing = true;
            Close();
        }
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closing)
        {
            return;
        }

        if (_promptingClose)
        {
            args.Cancel = true;
            return;
        }

        if (_document is null ||
            string.Equals(Editor.Text, _savedText, StringComparison.Ordinal))
        {
            return;
        }

        args.Cancel = true;
        _promptingClose = true;
        try
        {
            if (await ConfirmCloseAsync())
            {
                _closing = true;
                Close();
            }
        }
        finally
        {
            _promptingClose = false;
        }
    }

    private async Task<bool> ConfirmCloseAsync()
    {
        if (_document is null ||
            string.Equals(Editor.Text, _savedText, StringComparison.Ordinal))
        {
            return true;
        }

        ContentDialog dialog = new()
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = Strings.Get("NoteWindowUnsavedTitle"),
            Content = Strings.Get("NoteWindowUnsavedBody"),
            PrimaryButtonText = Strings.Get("NoteWindowSave"),
            SecondaryButtonText = Strings.Get("NoteWindowDiscard"),
            CloseButtonText = Strings.Get("CommonCancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        ContentDialogResult result = await DialogHost.ShowAsync(dialog);
        if (result == ContentDialogResult.Primary)
        {
            return await SaveAsync();
        }

        return result == ContentDialogResult.Secondary;
    }
}
