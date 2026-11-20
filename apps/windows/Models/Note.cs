using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace NopeNote.Windows.Models;

public sealed class Note : INotifyPropertyChanged
{
    private static readonly string NotesDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NopeNote",
        "Notes");

    private string _text = string.Empty;
    private DateTime _date = DateTime.Now;

    public Note()
    {
        FileName = "notes" + DateTime.Now.ToBinary().ToString() + ".txt";
    }

    public Note(string fileName, string text, DateTime date)
    {
        FileName = fileName;
        Text = text;
        Date = date;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FileName { get; }

    public string Text
    {
        get => _text;
        set
        {
            if (_text != value)
            {
                _text = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public DateTime Date
    {
        get => _date;
        set
        {
            if (_date != value)
            {
                _date = value;
                OnPropertyChanged();
            }
        }
    }

    public string DisplayTitle =>
        string.IsNullOrWhiteSpace(Text) ? "(Untitled note)" : Text.Split('\n')[0];

    public static Task<IReadOnlyList<Note>> LoadAllAsync()
    {
        List<Note> notes = new List<Note>();

        if (!Directory.Exists(NotesDirectory))
        {
            return Task.FromResult<IReadOnlyList<Note>>(notes);
        }

        foreach (string path in Directory.EnumerateFiles(NotesDirectory, "*.txt"))
        {
            string text = File.ReadAllText(path);
            notes.Add(new Note(Path.GetFileName(path), text, File.GetCreationTime(path)));
        }

        notes.Sort((left, right) => right.Date.CompareTo(left.Date));
        return Task.FromResult<IReadOnlyList<Note>>(notes);
    }

    public Task SaveAsync()
    {
        Directory.CreateDirectory(NotesDirectory);
        File.WriteAllText(Path.Combine(NotesDirectory, FileName), Text);
        return Task.CompletedTask;
    }

    public Task DeleteAsync()
    {
        string path = Path.Combine(NotesDirectory, FileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
