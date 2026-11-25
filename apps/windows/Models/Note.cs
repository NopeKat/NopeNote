using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using NopeNote.Core.Notes;
using NopeNote.Core.Vault;
using NopeNote.Windows.Helpers;

namespace NopeNote.Windows.Models;

/// <summary>
/// UI adapter for a vault note. A draft exists only in memory until its first save.
/// </summary>
public sealed class Note : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private string _savedText = string.Empty;
    private DateTimeOffset _updated;

    public Note()
    {
        IsDraft = true;
        Tags = Array.Empty<string>();
    }

    private Note(Guid id, string text, string title, IReadOnlyList<string> tags, bool pinned, DateTimeOffset updated)
    {
        Id = id;
        _text = text;
        _savedText = text;
        _updated = updated;
        DisplayTitle = title;
        Tags = tags;
        IsPinned = pinned;
        IsDraft = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; private set; }

    public bool IsDraft { get; private set; }

    public string DisplayTitle { get; private set; } = Strings.Get("NotesUntitled");

    public IReadOnlyList<string> Tags { get; private set; }

    public bool IsPinned { get; private set; }

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

    public bool HasUnsavedChanges
        => IsDraft || !string.Equals(Text, _savedText, StringComparison.Ordinal);

    public static async Task<IReadOnlyList<Note>> LoadAllAsync(IVaultStore store)
    {
        NoteScanResult scan = await store.ScanNotesAsync();
        var notes = new List<Note>(scan.Notes.Count);

        foreach (NoteSummary summary in scan.Notes)
        {
            NoteDocument? document = await store.ReadNoteAsync(summary.Id);
            if (document is null)
            {
                continue;
            }

            notes.Add(new Note(summary.Id, document.Body, summary.Title, summary.Tags, summary.Pinned, summary.Updated));
        }

        return notes;
    }

    public async Task SaveAsync(IVaultStore store)
    {
        if (!HasUnsavedChanges)
        {
            return;
        }

        NoteDocument document = IsDraft
            ? await store.CreateNoteAsync(string.Empty, Text)
            : await store.UpdateNoteAsync(Id, Text);

        if (IsDraft)
        {
            Id = document.Summary.Id;
            IsDraft = false;
        }

        _savedText = document.Body;
        _updated = document.Summary.Updated;
        DisplayTitle = document.Summary.Title;
        Tags = document.Summary.Tags;
        IsPinned = document.Summary.Pinned;
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
        DisplayTitle = document.Summary.Title;
        Tags = document.Summary.Tags;
        IsPinned = document.Summary.Pinned;
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Date));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(TagText));
        OnPropertyChanged(nameof(TagVisibility));
        OnPropertyChanged(nameof(PinVisibility));
    }

    private static string DeriveTitle(string text)
    {
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

            return trimmed.Length <= 80 ? trimmed : trimmed[..80];
        }

        return Strings.Get("NotesUntitled");
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
