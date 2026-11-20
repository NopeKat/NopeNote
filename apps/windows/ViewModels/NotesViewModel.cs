using System.Collections.ObjectModel;
using System.Threading.Tasks;
using NopeNote.Windows.Models;

namespace NopeNote.Windows.ViewModels;

public sealed class NotesViewModel
{
    private readonly ObservableCollection<Note> _allNotes = new ObservableCollection<Note>();

    public ObservableCollection<Note> Notes { get; } = new ObservableCollection<Note>();

    public bool IsEmpty => Notes.Count == 0;

    public bool IsLoaded { get; private set; }

    public async Task LoadAsync()
    {
        _allNotes.Clear();
        System.Collections.Generic.IReadOnlyList<Note> notes = await Note.LoadAllAsync();
        foreach (Note note in notes)
        {
            _allNotes.Add(note);
        }

        IsLoaded = true;
        ApplyFilter(CurrentQuery);
    }

    public void ApplyFilter(string query)
    {
        CurrentQuery = query;
        Notes.Clear();

        foreach (Note note in _allNotes)
        {
            if (string.IsNullOrWhiteSpace(query) ||
                note.Text.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0)
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

    public string CurrentQuery { get; private set; } = string.Empty;
}
