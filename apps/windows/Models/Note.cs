using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using NopeNote.Core.Notes;
using NopeNote.Core.Vault;
using NopeNote.Windows.Helpers;
using NopeNote.Windows.Services;

namespace NopeNote.Windows.Models;

/// <summary>
/// UI adapter for a vault note. A draft exists only in memory until its first save.
/// </summary>
public sealed class Note : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private string _savedText = string.Empty;
    private DateTimeOffset _updated;
    private string _contentHash = string.Empty;
    private bool _isSelected;
    private Visibility _selectionVisibility = Visibility.Collapsed;
    private string _folder = string.Empty;

    public Note()
    {
        IsDraft = true;
        Tags = Array.Empty<string>();
    }

    private Note(
        Guid id,
        string text,
        string title,
        IReadOnlyList<string> tags,
        bool pinned,
        string folder,
        DateTimeOffset updated,
        string contentHash = "")
    {
        Id = id;
        _text = text;
        _savedText = text;
        _updated = updated;
        _contentHash = contentHash;
        DisplayTitle = NoteTitle.Display(title);
        Tags = tags;
        IsPinned = pinned;
        Folder = folder;
        IsDraft = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; private set; }

    public bool IsDraft { get; private set; }

    public string DisplayTitle { get; private set; } = Strings.Get("NotesUntitled");

    public IReadOnlyList<string> Tags { get; private set; }

    public bool IsPinned { get; private set; }

    public string Folder
    {
        get => _folder;
        set
        {
            if (!string.Equals(_folder, value, StringComparison.Ordinal))
            {
                _folder = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FolderText));
                OnPropertyChanged(nameof(FolderVisibility));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public Visibility SelectionVisibility
    {
        get => _selectionVisibility;
        set
        {
            if (_selectionVisibility != value)
            {
                _selectionVisibility = value;
                OnPropertyChanged();
            }
        }
    }

    public string FolderText => _folder;

    public Visibility FolderVisibility => _folder.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    public string SelectionCheckName => Strings.Get("NotesSelectNote");

    public string TagText => string.Join(" · ", Tags);

    public Visibility TagVisibility => Tags.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

    public Visibility PinVisibility => IsPinned ? Visibility.Visible : Visibility.Collapsed;

    public string Text
    {
        get => _text;
        set
        {
            if (_text != value)
            {
                _text = value;
                DisplayTitle = DeriveTitle(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public DateTime Date => _updated.LocalDateTime;

    public DateTimeOffset Updated => _updated;

    public string ContentHash => _contentHash;

    public bool HasUnsavedChanges
        => IsDraft || !string.Equals(Text, _savedText, StringComparison.Ordinal);

    public static async Task<IReadOnlyList<Note>> LoadAllAsync(IVaultStore store, NoteSearchIndex index)
    {
        NoteScanResult scan = await store.ScanNotesAsync();
        index.Configure(store.RootPath, scan.Notes.Count);

        var notes = new List<Note>(scan.Notes.Count);
        var seen = new HashSet<Guid>();

        foreach (NoteSummary summary in scan.Notes)
        {
            string? cached = index.TryGetBody(summary.Id, summary.Updated);
            string? cachedHash = index.TryGetContentHash(summary.Id, summary.Updated);
            string body;
            string contentHash;
            if (cached is not null && cachedHash is not null)
            {
                body = cached;
                contentHash = cachedHash;
            }
            else
            {
                NoteDocument? document = await store.ReadNoteAsync(summary.Id);
                if (document is null)
                {
                    continue;
                }

                body = document.Body;
                contentHash = document.ContentHash;
                index.Remember(summary.Id, summary.Updated, body, contentHash);
            }

            seen.Add(summary.Id);
            notes.Add(new Note(
                summary.Id,
                body,
                summary.Title,
                summary.Tags,
                summary.Pinned,
                summary.Folder,
                summary.Updated,
                contentHash));
        }

        index.Prune(seen);
        _ = index.SaveAsync();
        return notes;
    }

    public async Task SaveAsync(IVaultStore store)
    {
        if (!HasUnsavedChanges)
        {
            return;
        }

        NoteDocument document = IsDraft
            ? await store.CreateNoteAsync(string.Empty, Text, folder: Folder)
            : await store.UpdateNoteAsync(
                Id,
                Text,
                expectedUpdated: _updated,
                expectedHash: _contentHash);

        if (IsDraft)
        {
            Id = document.Summary.Id;
            IsDraft = false;
        }

        _savedText = document.Body;
        _updated = document.Summary.Updated;
        _contentHash = document.ContentHash;
        DisplayTitle = NoteTitle.Display(document.Summary.Title);
        Tags = document.Summary.Tags;
        IsPinned = document.Summary.Pinned;
        Folder = document.Summary.Folder;
        OnPropertyChanged(nameof(Date));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(TagText));
        OnPropertyChanged(nameof(TagVisibility));
        OnPropertyChanged(nameof(PinVisibility));
    }

    public async Task DeleteAsync(IVaultStore store)
    {
        if (IsDraft)
        {
            return;
        }

        await store.TrashNoteAsync(Id);
    }

    /// <summary>恢复历史版本后同步内存状态，避免再触发一次多余保存。</summary>
    public void ApplyRestoredDocument(NoteDocument document)
    {
        _text = document.Body;
        _savedText = document.Body;
        _updated = document.Summary.Updated;
        _contentHash = document.ContentHash;
        DisplayTitle = NoteTitle.Display(document.Summary.Title);
        Tags = document.Summary.Tags;
        IsPinned = document.Summary.Pinned;
        Folder = document.Summary.Folder;
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Date));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(TagText));
        OnPropertyChanged(nameof(TagVisibility));
        OnPropertyChanged(nameof(PinVisibility));
    }

    /// <summary>After a conflict, rebase the optimistic token on what is on disk so
    /// the next explicit save no longer conflicts with the same revision. The local
    /// editor text is intentionally kept; only the saved baseline moves.</summary>
    public async Task RefreshBaseAsync(IVaultStore store)
    {
        NoteDocument? document = await store.ReadNoteAsync(Id);
        if (document is null)
        {
            return;
        }

        _savedText = document.Body;
        _updated = document.Summary.Updated;
        _contentHash = document.ContentHash;
        OnPropertyChanged(nameof(Date));
    }

    private static string DeriveTitle(string text)
    {
        string? fallback = null;
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                return trimmed[2..].Trim();
            }

            if (fallback is null)
            {
                string plain = trimmed.TrimStart('-', '*', '>', '#', ' ');
                fallback = plain.Length <= 80 ? plain : plain[..80];
            }
        }

        return fallback ?? Strings.Get("NotesUntitled");
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
