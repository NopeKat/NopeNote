using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;

namespace NopeNote.Windows.Models;

public sealed class Note
{
    private static readonly StorageFolder StorageFolder = ApplicationData.Current.LocalFolder;

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

    public string FileName { get; }

    public string Text { get; set; } = string.Empty;

    public DateTime Date { get; set; } = DateTime.Now;

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
}
