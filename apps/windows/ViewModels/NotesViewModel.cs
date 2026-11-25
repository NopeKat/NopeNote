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
        var filtered = new List<Note>(_allNotes.Count);

        foreach (Note note in _allNotes)
        {
            if (string.IsNullOrWhiteSpace(query) ||
                note.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                filtered.Add(note);
            }
        }

        Notes = filtered;
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
