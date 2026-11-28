using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
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
    private const int ExcerptLimit = 120;

    private string _text = string.Empty;
    private string _savedText = string.Empty;
    private string _excerpt = string.Empty;
    private DateTimeOffset _updated;
    private string _contentHash = string.Empty;
    private bool _isSelected;
    private bool _bodyLoaded;
    private NoteSearchIndex? _index;
    private Visibility _selectionVisibility = Visibility.Collapsed;
    private string _folder = string.Empty;

    public Note()
    {
        IsDraft = true;
        Tags = Array.Empty<string>();
        _bodyLoaded = true;
    }

    private Note(
        Guid id,
        string text,
        string title,
        IReadOnlyList<string> tags,
        bool pinned,
        string folder,
        DateTimeOffset updated,
        string contentHash = "",
        bool bodyLoaded = true,
        NoteSearchIndex? index = null)
    {
        Id = id;
        _text = text;
        _savedText = text;
        _excerpt = BuildExcerpt(text);
        _updated = updated;
        _contentHash = contentHash;
        _index = index;
        DisplayTitle = NoteTitle.Display(title);
        Tags = tags;
        IsPinned = pinned;
        Folder = folder;
        IsDraft = false;
        _bodyLoaded = bodyLoaded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; private set; }

    public bool IsDraft { get; private set; }

    /// <summary>False while the list only holds the summary; reading the body is deferred.</summary>
    public bool IsBodyLoaded => _bodyLoaded;

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
                _excerpt = BuildExcerpt(value);
                DisplayTitle = DeriveTitle(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(Excerpt));
                OnPropertyChanged(nameof(ExcerptVisibility));
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    /// <summary>One-line preview for the list, collapsed while the body is not loaded.</summary>
    public string Excerpt => _excerpt;

    public Visibility ExcerptVisibility
        => _excerpt.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    public DateTime Date => _updated.LocalDateTime;

    public DateTimeOffset Updated => _updated;

    public string ContentHash => _contentHash;

    public bool HasUnsavedChanges
        => IsDraft || !string.Equals(Text, _savedText, StringComparison.Ordinal);

    /// <summary>Builds the list from summaries only; bodies are read when a note is
    /// opened or when a query needs to search the text.</summary>
    public static async Task<IReadOnlyList<Note>> LoadAllAsync(IVaultStore store, NoteSearchIndex index)
    {
        NoteScanResult scan = await store.ScanNotesAsync();
        index.Configure(store.RootPath, scan.Notes.Count);

        var notes = new List<Note>(scan.Notes.Count);
        var seen = new HashSet<Guid>();

        foreach (NoteSummary summary in scan.Notes)
        {
            seen.Add(summary.Id);
            notes.Add(new Note(
                summary.Id,
                string.Empty,
                summary.Title,
                summary.Tags,
                summary.Pinned,
                summary.Folder,
                summary.Updated,
                bodyLoaded: false,
                index: index));
        }

        index.Prune(seen);
        _ = index.SaveAsync();
        return notes;
    }

    /// <summary>Reads the body once (cache first) and refreshes the summary fields
    /// from the document when the file was read.</summary>
    public async Task EnsureBodyAsync(IVaultStore store)
    {
        if (_bodyLoaded)
        {
            return;
        }

        string? cachedBody = _index?.TryGetBody(Id, _updated);
        string? cachedHash = _index?.TryGetContentHash(Id, _updated);
        NoteDocument? document = null;

        if (cachedBody is null || cachedHash is null)
        {
            document = await store.ReadNoteAsync(Id);
            if (document is null)
            {
                return;
            }

            cachedBody = document.Body;
            cachedHash = document.ContentHash;
            _index?.Remember(Id, document.Summary.Updated, document.Body, document.ContentHash);
        }

        _text = cachedBody;
        _savedText = cachedBody;
        _excerpt = BuildExcerpt(cachedBody);
        _contentHash = cachedHash;
        if (document is not null)
        {
            _updated = document.Summary.Updated;
            DisplayTitle = NoteTitle.Display(document.Summary.Title);
            Tags = document.Summary.Tags;
            IsPinned = document.Summary.Pinned;
            Folder = document.Summary.Folder;
        }

        _bodyLoaded = true;
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Excerpt));
        OnPropertyChanged(nameof(ExcerptVisibility));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(Date));
        OnPropertyChanged(nameof(TagText));
        OnPropertyChanged(nameof(TagVisibility));
        OnPropertyChanged(nameof(PinVisibility));
    }

    /// <summary>Collapses markdown line breaks into one line and trims it.</summary>
    private static string BuildExcerpt(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(ExcerptLimit + 8);
        foreach (string line in body.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(trimmed);
            if (builder.Length >= ExcerptLimit)
            {
                break;
            }
        }

        return builder.Length <= ExcerptLimit
            ? builder.ToString()
            : builder.ToString(0, ExcerptLimit) + "…";
    }

    public async Task SaveAsync(IVaultStore store)
    {
        // A note whose body was never loaded has an empty editor; saving it would
        // overwrite the file, so such a note is never persisted before EnsureBodyAsync.
        if (!_bodyLoaded || !HasUnsavedChanges)
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
