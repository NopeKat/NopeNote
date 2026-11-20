using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.Storage;

namespace NopeNote.Windows.Models;

public sealed class Note : INotifyPropertyChanged
{
    private static readonly StorageFolder StorageFolder = ApplicationData.Current.LocalFolder;
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

    public static async Task<IReadOnlyList<Note>> LoadAllAsync()
    {
        IReadOnlyList<IStorageItem> items = await StorageFolder.GetItemsAsync();
        List<Note> notes = new List<Note>();

        foreach (IStorageItem item in items)
        {
            if (item is StorageFile file && file.FileType == ".txt")
            {
                string text = await FileIO.ReadTextAsync(file);
                notes.Add(new Note(file.Name, text, file.DateCreated.DateTime));
            }
        }

        notes.Sort((left, right) => right.Date.CompareTo(left.Date));
        return notes;
    }

    public async Task SaveAsync()
    {
        StorageFile file = await StorageFolder.CreateFileAsync(
            FileName,
            CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(file, Text);
    }

    public async Task DeleteAsync()
    {
        StorageFile? file = (StorageFile?)await StorageFolder.TryGetItemAsync(FileName);
        if (file is not null)
        {
            await file.DeleteAsync();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
