using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using NopeNote.Core.Vault;
using NopeNote.Windows.Models;

namespace NopeNote.Windows.ViewModels;

public sealed class NotesViewModel : INotifyPropertyChanged
{
    private readonly List<Note> _allNotes = new();

    private IVaultStore? _store;
    private IReadOnlyList<Note> _notes = Array.Empty<Note>();

    public event PropertyChangedEventHandler? PropertyChanged;

    // 过滤结果整份替换、一次通知，避免清空后逐项添加时触发 N 次集合变更。
    public IReadOnlyList<Note> Notes
    {
        get => _notes;
        private set
        {
            _notes = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Notes)));
        }
    }

    public IVaultStore Store
        => _store ?? throw new InvalidOperationException("the vault store has not been attached yet.");

    public IVaultStore? StoreOrNull => _store;

    public bool IsEmpty => Notes.Count == 0;

    public bool IsLoaded { get; private set; }

    public bool IsLoading { get; private set; }

    public string CurrentQuery { get; private set; } = string.Empty;

    public string? TagFilter { get; private set; }

    public void SetTagFilter(string? tag)
    {
        TagFilter = string.IsNullOrWhiteSpace(tag) ? null : tag;
        ApplyFilter(CurrentQuery);
    }

    public IReadOnlyList<string> GetAvailableTags()
    {
        var tags = new List<string>();
        foreach (Note note in _allNotes)
        {
            foreach (string tag in note.Tags)
            {
                if (!tags.Exists(existing =>
                        string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase)))
                {
                    tags.Add(tag);
                }
            }
        }

        tags.Sort(StringComparer.OrdinalIgnoreCase);
        return tags;
    }

    public void Attach(IVaultStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            _allNotes.Clear();
            IReadOnlyList<Note> notes = await Note.LoadAllAsync(Store);
            foreach (Note note in notes)
            {
                _allNotes.Add(note);
            }

            IsLoaded = true;
            ApplyFilter(CurrentQuery);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void ApplyFilter(string query)
    {
        CurrentQuery = query;
        var pinned = new List<Note>(_allNotes.Count);
        var others = new List<Note>(_allNotes.Count);

        foreach (Note note in _allNotes)
        {
            if (!Matches(note, query))
            {
                continue;
            }

            (note.IsPinned ? pinned : others).Add(note);
        }

        // 列表顺序本身是“最近更新在前”，这里只把置顶的一组提到最前面。
        pinned.AddRange(others);
        Notes = pinned;
    }

    private bool Matches(Note note, string query)
    {
        if (TagFilter is not null && !HasTag(note, TagFilter))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        if (note.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            note.DisplayTitle.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        foreach (string tag in note.Tags)
        {
            if (tag.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasTag(Note note, string tag)
    {
        foreach (string existing in note.Tags)
        {
            if (string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // 只维护数据源，列表重建交给调用方统一做一次（RefreshNotes），
    // 避免新建/删除时在 ViewModel 和页面各过滤一遍。
    public void Add(Note note)
    {
        _allNotes.Insert(0, note);
    }

    public void Remove(Note note)
    {
        _allNotes.Remove(note);
    }
}
