using NopeNote.Core.Notes;

namespace NopeNote.Core.Vault;

/// <summary>Reads and writes active notes under <c>.vault/notes/</c>, including the
/// conflict copies produced when an optimistic update loses its race.</summary>
internal sealed class NoteFileStore
{
    private readonly VaultStorage _storage;
    private readonly HistoryStore _history;
    private readonly string _deviceId;

    internal NoteFileStore(VaultStorage storage, HistoryStore history, string deviceId)
    {
        _storage = storage;
        _history = history;
        _deviceId = deviceId;
    }

    internal Task<NoteScanResult> ScanAsync(CancellationToken cancellationToken)
        => _storage.RunAsync(() => Scan(cancellationToken), VaultStorage.ReadTimeout, cancellationToken);

    private NoteScanResult Scan(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var notes = new List<NoteSummary>();
        var issues = new List<VaultIssue>();

        if (!Directory.Exists(_storage.NotesDirectory))
        {
            return new NoteScanResult(notes, issues);
        }

        foreach (string path in Directory.EnumerateFiles(
                     _storage.NotesDirectory,
                     "*.md",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = $".vault/notes/{Path.GetFileName(path)}";

            if (!VaultStorage.TryParseCanonicalId(Path.GetFileNameWithoutExtension(path), out Guid id))
            {
                issues.Add(new VaultIssue(relativePath, "file name is not a canonical UUID."));
                continue;
            }

            try
            {
                NoteDocument document = VaultStorage.ReadDocument(id, relativePath, File.ReadAllText(path));
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

        return new NoteScanResult(notes, issues);
    }

    internal Task<NoteDocument?> ReadAsync(Guid id, CancellationToken cancellationToken)
        => _storage.RunAsync(() => Read(id, cancellationToken), VaultStorage.ReadTimeout, cancellationToken);

    private NoteDocument? Read(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? text = ReadNoteFile(id);
        if (text is null)
        {
            return null;
        }

        return VaultStorage.ReadDocument(id, VaultStorage.NoteRelativePath(id), text);
    }

    internal Task<NoteDocument> CreateAsync(
        string title,
        string body,
        IReadOnlyList<string>? tags,
        bool pinned,
        DateTimeOffset? createdAt,
        DateTimeOffset? updatedAt,
        string? folder,
        CancellationToken cancellationToken)
        => _storage.RunAsync(
            () => Create(title, body, tags, pinned, createdAt, updatedAt, folder, cancellationToken),
            VaultStorage.WriteTimeout,
            cancellationToken);

    private NoteDocument Create(
        string title,
        string body,
        IReadOnlyList<string>? tags,
        bool pinned,
        DateTimeOffset? createdAt,
        DateTimeOffset? updatedAt,
        string? folder,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _storage.EnsureWritable();

        Guid id = Guid.NewGuid();
        DateTimeOffset created = createdAt ?? VaultStorage.UtcNowSeconds();
        DateTimeOffset updated = updatedAt ?? (created > VaultStorage.UtcNowSeconds() ? created : VaultStorage.UtcNowSeconds());
        if (updated < created)
        {
            updated = created;
        }

        string text = NoteFrontMatterWriter.Build(
            id,
            created,
            updated,
            title,
            tags ?? Array.Empty<string>(),
            pinned,
            body,
            folder: folder);

        _storage.WriteText(_storage.NotePath(id), text);
        return VaultStorage.ReadDocument(id, VaultStorage.NoteRelativePath(id), text);
    }

    internal Task<NoteDocument> UpdateAsync(
        Guid id,
        string? body,
        string? title,
        IReadOnlyList<string>? tags,
        bool? pinned,
        DateTimeOffset? expectedUpdated,
        string? folder,
        CancellationToken cancellationToken)
        => _storage.RunAsync(
            () => Update(id, body, title, tags, pinned, expectedUpdated, folder, cancellationToken),
            VaultStorage.WriteTimeout,
            cancellationToken);

    private NoteDocument Update(
        Guid id,
        string? body,
        string? title,
        IReadOnlyList<string>? tags,
        bool? pinned,
        DateTimeOffset? expectedUpdated,
        string? folder,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _storage.EnsureWritable();

        string? currentText = ReadNoteFile(id);
        if (currentText is null)
        {
            throw new VaultNoteNotFoundException(id);
        }

        NoteDocument current = VaultStorage.ReadDocument(id, VaultStorage.NoteRelativePath(id), currentText);
        DateTimeOffset updated = VaultStorage.NextUpdated(current.Summary.Updated);
        string noteBody = body ?? current.Body;

        string text = NoteFrontMatterWriter.Build(
            id,
            current.Summary.Created,
            updated,
            title ?? current.Summary.Title,
            tags ?? current.Summary.Tags,
            pinned ?? current.Summary.Pinned,
            noteBody,
            current.FrontMatterLines,
            folder: folder ?? current.Summary.Folder);

        if (expectedUpdated.HasValue && expectedUpdated.Value != current.Summary.Updated)
        {
            string conflictPath = WriteConflictCopy(id, text);
            throw new VaultConflictException(conflictPath);
        }

        _history.WriteSnapshotIfNeeded(id, currentText, updated);
        _storage.WriteText(_storage.NotePath(id), text, currentText);

        return VaultStorage.ReadDocument(id, VaultStorage.NoteRelativePath(id), text);
    }

    private string WriteConflictCopy(Guid id, string text)
    {
        string name = $"{id:D}.{_deviceId}." +
            NoteFrontMatterWriter.FormatTimestamp(VaultStorage.UtcNowSeconds())
                .Replace("-", string.Empty)
                .Replace(":", string.Empty) +
            ".md";
        string relativePath = $".sync/conflicts/{name}";
        _storage.WriteText(Path.Combine(_storage.ConflictsDirectory, name), text);
        return relativePath;
    }

    private string? ReadNoteFile(Guid id)
    {
        string path = _storage.NotePath(id);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
