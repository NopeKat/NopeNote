using System.Globalization;
using System.Text;
using NopeNote.Core.Notes;

namespace NopeNote.Core.Vault;

public sealed class LocalVaultStore : IVaultStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly TimeSpan SnapshotThrottle = TimeSpan.FromMinutes(5);

    private readonly string _notesDirectory;
    private readonly string _trashDirectory;
    private readonly string _historyDirectory;
    private readonly string _conflictsDirectory;
    private readonly string _deviceId;

    public LocalVaultStore(
        string rootPath,
        VaultMetadata metadata,
        VaultCapabilities capabilities,
        string deviceId = "localdev")
    {
        RootPath = rootPath;
        Metadata = metadata;
        Capabilities = capabilities;
        _deviceId = deviceId;
        _notesDirectory = Path.Combine(rootPath, ".vault", "notes");
        _trashDirectory = Path.Combine(rootPath, ".trash");
        _historyDirectory = Path.Combine(rootPath, ".history");
        _conflictsDirectory = Path.Combine(rootPath, ".sync", "conflicts");
    }

    public string RootPath { get; }

    public VaultMetadata Metadata { get; }

    public VaultCapabilities Capabilities { get; }

    public Task<NoteScanResult> ScanNotesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var notes = new List<NoteSummary>();
        var issues = new List<VaultIssue>();

        if (!Directory.Exists(_notesDirectory))
        {
            return Task.FromResult(new NoteScanResult(notes, issues));
        }

        foreach (string path in Directory.EnumerateFiles(_notesDirectory, "*.md", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = $".vault/notes/{Path.GetFileName(path)}";

            if (!TryParseCanonicalId(Path.GetFileNameWithoutExtension(path), out Guid id))
            {
                issues.Add(new VaultIssue(relativePath, "file name is not a canonical UUID."));
                continue;
            }

            try
            {
                NoteDocument document = ReadDocument(id, relativePath, File.ReadAllText(path));
                notes.Add(document.Summary);
            }
            catch (VaultFormatException exception)
            {
                issues.Add(new VaultIssue(relativePath, exception.Message));
            }
            catch (IOException exception)
            {
                issues.Add(new VaultIssue(relativePath, exception.Message));
            }
        }

        notes.Sort(static (left, right) =>
        {
            int byUpdated = right.Updated.CompareTo(left.Updated);
            return byUpdated != 0 ? byUpdated : right.Id.CompareTo(left.Id);
        });

        return Task.FromResult(new NoteScanResult(notes, issues));
    }

    public Task<NoteDocument?> ReadNoteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? text = ReadNoteFile(id);
        if (text is null)
        {
            return Task.FromResult<NoteDocument?>(null);
        }

        return Task.FromResult<NoteDocument?>(ReadDocument(id, NoteRelativePath(id), text));
    }

    public Task<NoteDocument> CreateNoteAsync(
        string title,
        string body,
        IReadOnlyList<string>? tags = null,
        bool pinned = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWritable();

        Guid id = Guid.NewGuid();
        DateTimeOffset now = UtcNowSeconds();
        string text = NoteFrontMatterWriter.Build(
            id,
            now,
            now,
            title,
            tags ?? Array.Empty<string>(),
            pinned,
            body);

        WriteText(NotePath(id), text);
        return Task.FromResult(ReadDocument(id, NoteRelativePath(id), text));
    }

    public Task<NoteDocument> UpdateNoteAsync(
        Guid id,
        string? body = null,
        string? title = null,
        IReadOnlyList<string>? tags = null,
        bool? pinned = null,
        DateTimeOffset? expectedUpdated = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWritable();

        string? currentText = ReadNoteFile(id);
        if (currentText is null)
        {
            throw new VaultNoteNotFoundException(id);
        }

        NoteDocument current = ReadDocument(id, NoteRelativePath(id), currentText);
        DateTimeOffset updated = NextUpdated(current.Summary.Updated);
        string noteBody = body ?? current.Body;

        string text = NoteFrontMatterWriter.Build(
            id,
            current.Summary.Created,
            updated,
            title ?? current.Summary.Title,
            tags ?? current.Summary.Tags,
            pinned ?? current.Summary.Pinned,
            noteBody,
            current.FrontMatterLines);

        if (expectedUpdated.HasValue && expectedUpdated.Value != current.Summary.Updated)
        {
            string conflictPath = WriteConflictCopy(id, text);
            throw new VaultConflictException(conflictPath);
        }

        WriteSnapshotIfNeeded(id, currentText, updated);
        WriteText(NotePath(id), text, currentText);

        return Task.FromResult(ReadDocument(id, NoteRelativePath(id), text));
    }

    public Task TrashNoteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWritable();

        string? currentText = ReadNoteFile(id);
        if (currentText is null)
        {
            throw new VaultNoteNotFoundException(id);
        }

        NoteDocument current = ReadDocument(id, NoteRelativePath(id), currentText);
        DateTimeOffset deletedAt = UtcNowSeconds();

        string text = NoteFrontMatterWriter.Build(
            id,
            current.Summary.Created,
            current.Summary.Updated,
            current.Summary.Title,
            current.Summary.Tags,
            current.Summary.Pinned,
            current.Body,
            current.FrontMatterLines,
            deletedAt);

        string trashPath = TrashPath(id);
        WriteText(trashPath, text);
        File.Delete(NotePath(id));

        return Task.CompletedTask;
    }

    public Task RestoreNoteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWritable();

        string trashPath = TrashPath(id);
        if (!File.Exists(trashPath))
        {
            throw new VaultNoteNotFoundException(id);
        }

        NoteDocument trashed = ReadDocument(id, TrashRelativePath(id), File.ReadAllText(trashPath));
        DateTimeOffset updated = NextUpdated(trashed.Summary.Updated);

        string text = NoteFrontMatterWriter.Build(
            id,
            trashed.Summary.Created,
            updated,
            trashed.Summary.Title,
            trashed.Summary.Tags,
            trashed.Summary.Pinned,
            trashed.Body,
            trashed.FrontMatterLines);

        WriteText(NotePath(id), text);
        File.Delete(trashPath);

        return Task.CompletedTask;
    }

    public Task PurgeNoteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWritable();

        bool touched = false;

        string notePath = NotePath(id);
        if (File.Exists(notePath))
        {
            File.Delete(notePath);
            touched = true;
        }

        string trashPath = TrashPath(id);
        if (File.Exists(trashPath))
        {
            File.Delete(trashPath);
            touched = true;
        }

        string historyPath = Path.Combine(_historyDirectory, id.ToString("D"));
        if (Directory.Exists(historyPath))
        {
            Directory.Delete(historyPath, recursive: true);
            touched = true;
        }

        if (!touched)
        {
            throw new VaultNoteNotFoundException(id);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VaultSnapshotInfo>> ListHistoryAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshots = new List<VaultSnapshotInfo>();
        string directory = Path.Combine(_historyDirectory, id.ToString("D"));
        if (!Directory.Exists(directory))
        {
            return Task.FromResult<IReadOnlyList<VaultSnapshotInfo>>(snapshots);
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
        return Task.FromResult<IReadOnlyList<VaultSnapshotInfo>>(snapshots);
    }

    public Task<NoteDocument?> ReadHistoryAsync(
        Guid id,
        string snapshotName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string path = Path.Combine(_historyDirectory, id.ToString("D"), snapshotName);
        if (!File.Exists(path))
        {
            return Task.FromResult<NoteDocument?>(null);
        }

        string relativePath = $".history/{id:D}/{snapshotName}";
        return Task.FromResult<NoteDocument?>(ReadDocument(id, relativePath, File.ReadAllText(path)));
    }

    private void WriteSnapshotIfNeeded(Guid id, string currentText, DateTimeOffset updated)
    {
        string directory = Path.Combine(_historyDirectory, id.ToString("D"));
        Directory.CreateDirectory(directory);

        string? latest = FindLatestSnapshot(directory);
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

        WriteText(target, currentText);
    }

    private string WriteConflictCopy(Guid id, string text)
    {
        string name = $"{id:D}.{_deviceId}.{NoteFrontMatterWriter.FormatTimestamp(UtcNowSeconds()).Replace("-", string.Empty).Replace(":", string.Empty)}.md";
        string relativePath = $".sync/conflicts/{name}";
        WriteText(Path.Combine(_conflictsDirectory, name), text);
        return relativePath;
    }

    private void EnsureWritable()
    {
        if (!Capabilities.Writable)
        {
            throw new VaultReadOnlyException("vault was opened read-only; writes are disabled.");
        }
    }

    private void WriteText(string path, string text, string? fallbackText = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (Capabilities.AtomicRename)
        {
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                File.WriteAllText(temporary, text, Utf8NoBom);
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }

            return;
        }

        File.WriteAllText(path, text, Utf8NoBom);
        string readBack = File.ReadAllText(path);
        if (string.Equals(readBack, text, StringComparison.Ordinal))
        {
            return;
        }

        if (fallbackText is not null)
        {
            File.WriteAllText(path, fallbackText, Utf8NoBom);
        }

        throw new IOException($"write verification failed for {Relative(path)}.");
    }

    private string? ReadNoteFile(Guid id)
    {
        string path = NotePath(id);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private string NotePath(Guid id) => Path.Combine(_notesDirectory, id.ToString("D") + ".md");

    private string TrashPath(Guid id) => Path.Combine(_trashDirectory, id.ToString("D") + ".md");

    private static string NoteRelativePath(Guid id) => $".vault/notes/{id:D}.md";

    private static string TrashRelativePath(Guid id) => $".trash/{id:D}.md";

    private static string? FindLatestSnapshot(string directory)
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

    private static DateTimeOffset NextUpdated(DateTimeOffset previous)
    {
        DateTimeOffset candidate = UtcNowSeconds();
        return candidate > previous ? candidate : previous.AddSeconds(1);
    }

    private static DateTimeOffset UtcNowSeconds()
    {
        long ticks = DateTime.UtcNow.Ticks;
        ticks -= ticks % TimeSpan.TicksPerSecond;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private string Relative(string path) => Path.GetRelativePath(RootPath, path).Replace('\\', '/');

    private static NoteDocument ReadDocument(Guid id, string relativePath, string text)
        => FrontMatterParser.Parse(id, relativePath, text);

    private static bool TryParseCanonicalId(string name, out Guid id)
    {
        id = Guid.Empty;
        if (!Guid.TryParseExact(name, "D", out Guid parsed))
        {
            return false;
        }

        string canonical = parsed.ToString("D");
        if (!string.Equals(name, canonical, StringComparison.Ordinal))
        {
            return false;
        }

        id = parsed;
        return true;
    }
}
