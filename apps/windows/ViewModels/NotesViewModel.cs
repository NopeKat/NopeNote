using System.Collections.ObjectModel;
using System.Threading.Tasks;
using NopeNote.Windows.Models;

namespace NopeNote.Windows.ViewModels;

public sealed class NotesViewModel
{
    public ObservableCollection<Note> Notes { get; } = new ObservableCollection<Note>();

    public bool IsEmpty => Notes.Count == 0;

    public async Task LoadAsync()
    {
        Notes.Clear();
        System.Collections.Generic.IReadOnlyList<Note> notes = await Note.LoadAllAsync();
        foreach (Note note in notes)
        {
            Notes.Add(note);
        }
    }

    public void Add(Note note)
    {
        Notes.Insert(0, note);
    }

    public void Remove(Note note)
    {
        Notes.Remove(note);
    }
}
