using NopeNote.Core.Notes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NopeNote.Windows.Services;

/// <summary>
/// Body cache under <c>%LOCALAPPDATA%\NopeNote\Runtime\</c>, switched on once a vault grows
/// past 2000 notes or lives on a network share (spec 14.5). It only speeds up loading;
/// every hit is validated against the note's updated stamp and a miss falls back to a real read.
/// </summary>
public sealed class NoteSearchIndex
{
    public const int NoteThreshold = 2000;

    private const int FormatVersion = 1;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, IndexRow> _rows = new();
    private string? _root;
    private string? _filePath;
    private bool _loaded;
    private bool _enabled;
    private bool _dirty;
    private Task _saveTask = Task.CompletedTask;

    public static NoteSearchIndex Current { get; } = new();

    public bool IsEnabled
    {
        get
        {
            lock (_gate)
            {
                return _enabled;
            }
        }
    }

    public void Configure(string vaultRoot, int noteCount)
    {
        string root = NormalizeRoot(vaultRoot);
        string filePath = ResolveFilePath(root);
        bool enabled = noteCount > NoteThreshold || IsNetworkPath(root);

        lock (_gate)
        {
            if (_root is not null && !string.Equals(_root, root, StringComparison.OrdinalIgnoreCase))
            {
                _rows.Clear();
                _loaded = false;
                _dirty = false;
            }

            _root = root;
            _filePath = filePath;
            _enabled = enabled;
            if (!enabled)
            {
                _rows.Clear();
                _dirty = false;
            }
        }
    }

    /// <summary>Returns the cached body only when the note has not been written since.</summary>
    public string? TryGetBody(Guid id, DateTimeOffset updated)
    {
        lock (_gate)
        {
            if (!_enabled)
            {
                return null;
            }

            EnsureLoaded();
            IndexRow? row = _rows.GetValueOrDefault(id);
            return row is not null && row.Updated == updated ? row.Body : null;
        }
    }

    public void Remember(Guid id, DateTimeOffset updated, string body)
    {
        lock (_gate)
        {
            if (!_enabled)
            {
                return;
            }

            EnsureLoaded();
            _rows[id] = new IndexRow(id, updated, body);
            _dirty = true;
        }
    }

    public void Prune(HashSet<Guid> keep)
    {
        lock (_gate)
        {
            if (!_enabled)
            {
                return;
            }

            EnsureLoaded();
            var stale = new List<Guid>();
            foreach (Guid id in _rows.Keys)
            {
                if (!keep.Contains(id))
                {
                    stale.Add(id);
                }
            }

            foreach (Guid id in stale)
            {
                _rows.Remove(id);
            }

            if (stale.Count > 0)
            {
                _dirty = true;
            }
        }
    }

    /// <summary>Writes the cache in the background; safe to call after every load.</summary>
    public Task SaveAsync()
    {
        List<IndexRow> snapshot;
        string path;
        lock (_gate)
        {
            if (!_enabled || !_dirty || _filePath is null)
            {
                return Task.CompletedTask;
            }

            _dirty = false;
            snapshot = new List<IndexRow>(_rows.Values);
            path = _filePath;
            IndexFile payload = new() { Version = FormatVersion, Entries = snapshot };
            _saveTask = Task.Run(() => WriteFile(path, payload));
            return _saveTask;
        }
    }

    public static bool IsNetworkPath(string vaultRoot)
    {
        if (string.IsNullOrWhiteSpace(vaultRoot))
        {
            return false;
        }

        if (vaultRoot.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            string? volume = Path.GetPathRoot(Path.GetFullPath(vaultRoot));
            return !string.IsNullOrEmpty(volume) && new DriveInfo(volume).DriveType == DriveType.Network;
        }
        catch (Exception exception) when (
            exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded || _filePath is null)
        {
            return;
        }

        _loaded = true;
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            IndexFile? file = JsonSerializer.Deserialize<IndexFile>(File.ReadAllText(_filePath));
            if (file is null || file.Version != FormatVersion)
            {
                return;
            }

            foreach (IndexRow row in file.Entries)
            {
                _rows[row.Id] = row;
            }
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _rows.Clear();
        }
    }

    private static void WriteFile(string path, IndexFile payload)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(payload));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // The cache is derived data; losing a write must never break note loading.
        }
    }

    private static string NormalizeRoot(string vaultRoot) => Path
        .GetFullPath(vaultRoot)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        .ToLowerInvariant();

    private static string ResolveFilePath(string root)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root))
            .AsSpan(0, 8)).ToLowerInvariant();
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string directory = Path.Combine(local, "NopeNote", "Runtime", key);
        return Path.Combine(directory, "search-index.json");
    }

    private sealed class IndexFile
    {
        public int Version { get; set; }

        public List<IndexRow> Entries { get; set; } = new();
    }

    private sealed class IndexRow
    {
        public IndexRow()
        {
        }

        public IndexRow(Guid id, DateTimeOffset updated, string body)
        {
            Id = id;
            Updated = updated;
            Body = body;
        }

        public Guid Id { get; set; }

        public DateTimeOffset Updated { get; set; }

        public string Body { get; set; } = string.Empty;
    }
}
