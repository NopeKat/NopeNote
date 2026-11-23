using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using NopeNote.Core.Vault;
using NopeNote.Windows.Models;

namespace NopeNote.Windows.ViewModels;

public sealed class NotesViewModel
{
    private readonly ObservableCollection<Note> _allNotes = new();

    private IVaultStore? _store;

    public ObservableCollection<Note> Notes { get; } = new();

    public IVaultStore Store
        => _store ?? throw new InvalidOperationException("the vault store has not been attached yet.");

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
        Notes.Clear();

        foreach (Note note in _allNotes)
        {
            if (string.IsNullOrWhiteSpace(query) ||
                note.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Notes.Add(note);
            }
        }
    }

    public void Add(Note note)
    {
        _allNotes.Insert(0, note);
        ApplyFilter(CurrentQuery);
    }

    public void Remove(Note note)
    {
        _allNotes.Remove(note);
        ApplyFilter(CurrentQuery);
    }
}
