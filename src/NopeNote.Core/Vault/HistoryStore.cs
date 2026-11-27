using System.Globalization;
using NopeNote.Core.Notes;

namespace NopeNote.Core.Vault;

/// <summary>Snapshot history under <c>.history/&lt;id&gt;/</c>: one file per saved revision,
/// throttled to at most one snapshot per minute.</summary>
internal sealed class HistoryStore
{
    private static readonly TimeSpan SnapshotThrottle = TimeSpan.FromMinutes(1);

    private readonly VaultStorage _storage;

    internal HistoryStore(VaultStorage storage)
    {
        _storage = storage;
    }

    internal Task<IReadOnlyList<VaultSnapshotInfo>> ListAsync(
        Guid id,
        CancellationToken cancellationToken)
        => _storage.RunAsync(() => List(id, cancellationToken), VaultStorage.ReadTimeout, cancellationToken);

    internal Task<NoteDocument?> ReadAsync(
        Guid id,
        string snapshotName,
        CancellationToken cancellationToken)
        => _storage.RunAsync(() => Read(id, snapshotName, cancellationToken), VaultStorage.ReadTimeout, cancellationToken);

    /// <summary>Called inside the IO gate while a note is being rewritten.</summary>
    internal void WriteSnapshotIfNeeded(Guid id, string currentText, DateTimeOffset updated)
    {
        string directory = _storage.HistoryPath(id);
        Directory.CreateDirectory(directory);

        string? latest = FindLatest(directory);
        if (latest is not null)
        {
            string latestText = File.ReadAllText(latest);
            if (string.Equals(latestText, currentText, StringComparison.Ordinal))
            {
                return;
            }

            if (TryParseSnapshotName(Path.GetFileName(latest), out DateTimeOffset takenAt) &&
                updated - takenAt < SnapshotThrottle)
            {
                return;
            }
        }

        DateTimeOffset stamp = updated;
        string target = SnapshotPath(directory, stamp);
        while (File.Exists(target))
        {
            stamp = stamp.AddSeconds(1);
            target = SnapshotPath(directory, stamp);
        }

        _storage.WriteText(target, currentText);
    }

    /// <summary>Called inside the IO gate when a note is purged. Returns false when no
    /// history existed for the note.</summary>
    internal bool DeleteAll(Guid id)
    {
        string directory = _storage.HistoryPath(id);
        if (!Directory.Exists(directory))
        {
            return false;
        }

        Directory.Delete(directory, recursive: true);
        return true;
    }

    private IReadOnlyList<VaultSnapshotInfo> List(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshots = new List<VaultSnapshotInfo>();
        string directory = _storage.HistoryPath(id);
        if (!Directory.Exists(directory))
        {
            return snapshots;
        }

        foreach (string path in Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(path);
            if (!TryParseSnapshotName(name, out DateTimeOffset takenAt))
            {
                continue;
            }

            snapshots.Add(new VaultSnapshotInfo(name, $".history/{id:D}/{name}", takenAt));
        }

        snapshots.Sort(static (left, right) => left.TakenAt.CompareTo(right.TakenAt));
        return snapshots;
    }

    private NoteDocument? Read(Guid id, string snapshotName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string path = Path.Combine(_storage.HistoryPath(id), snapshotName);
        if (!File.Exists(path))
        {
            return null;
        }

        string relativePath = $".history/{id:D}/{snapshotName}";
        return VaultStorage.ReadDocument(id, relativePath, File.ReadAllText(path));
    }

    private static string? FindLatest(string directory)
    {
        string? latest = null;
        DateTimeOffset latestStamp = DateTimeOffset.MinValue;

        foreach (string path in Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly))
        {
            if (!TryParseSnapshotName(Path.GetFileName(path), out DateTimeOffset takenAt))
            {
                continue;
            }

            if (latest is null || takenAt > latestStamp)
            {
                latest = path;
                latestStamp = takenAt;
            }
        }

        return latest;
    }

    private static string SnapshotPath(string directory, DateTimeOffset stamp)
        => Path.Combine(
            directory,
            stamp.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + ".md");

    private static bool TryParseSnapshotName(string name, out DateTimeOffset takenAt)
    {
        takenAt = default;
        if (!name.EndsWith(".md", StringComparison.Ordinal))
        {
            return false;
        }

        return DateTimeOffset.TryParseExact(
            name[..^3],
            "yyyyMMdd'T'HHmmss'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out takenAt);
    }
}
