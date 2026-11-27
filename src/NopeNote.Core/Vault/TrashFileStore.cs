using System.Globalization;
using NopeNote.Core.Notes;

namespace NopeNote.Core.Vault;

/// <summary>Moves notes between the active set and <c>.trash/</c>, and purges them for good.</summary>
internal sealed class TrashFileStore
{
    private readonly VaultStorage _storage;
    private readonly HistoryStore _history;

    internal TrashFileStore(VaultStorage storage, HistoryStore history)
    {
        _storage = storage;
        _history = history;
    }

    internal Task TrashAsync(Guid id, CancellationToken cancellationToken)
        => _storage.RunAsync(() => Trash(id, cancellationToken), VaultStorage.WriteTimeout, cancellationToken);

    private void Trash(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _storage.EnsureWritable();

        string notePath = _storage.NotePath(id);
        string? currentText = File.Exists(notePath) ? File.ReadAllText(notePath) : null;
        if (currentText is null)
        {
            throw new VaultNoteNotFoundException(id);
        }

        NoteDocument current = VaultStorage.ReadDocument(id, VaultStorage.NoteRelativePath(id), currentText);
        DateTimeOffset deletedAt = VaultStorage.UtcNowSeconds();

        string text = NoteFrontMatterWriter.Build(
            id,
            current.Summary.Created,
            current.Summary.Updated,
            NoteFrontMatterWriter.TitleForWrite(current.FrontMatterLines, current.Summary.Title, null),
            current.Summary.Tags,
            current.Summary.Pinned,
            current.Body,
            current.FrontMatterLines,
            deletedAt,
            current.Summary.Folder);

        // One rename and then an in-place rewrite: if the process dies between the two
        // steps the note carries no deletedAt yet, but it never exists in both places.
        string trashPath = _storage.TrashPath(id);
        VaultStorage.MoveVaultFile(notePath, trashPath);
        try
        {
            _storage.WriteText(trashPath, text, currentText);
        }
        catch
        {
            VaultStorage.MoveVaultFile(trashPath, notePath);
            throw;
        }
    }

    internal Task RestoreAsync(Guid id, CancellationToken cancellationToken)
        => _storage.RunAsync(() => Restore(id, cancellationToken), VaultStorage.WriteTimeout, cancellationToken);

    private void Restore(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _storage.EnsureWritable();

        string trashPath = _storage.TrashPath(id);
        if (!File.Exists(trashPath))
        {
            throw new VaultNoteNotFoundException(id);
        }

        string trashText = File.ReadAllText(trashPath);
        NoteDocument trashed = VaultStorage.ReadDocument(id, VaultStorage.TrashRelativePath(id), trashText);
        DateTimeOffset updated = VaultStorage.NextUpdated(trashed.Summary.Updated);

        string text = NoteFrontMatterWriter.Build(
            id,
            trashed.Summary.Created,
            updated,
            NoteFrontMatterWriter.TitleForWrite(trashed.FrontMatterLines, trashed.Summary.Title, null),
            trashed.Summary.Tags,
            trashed.Summary.Pinned,
            trashed.Body,
            trashed.FrontMatterLines,
            folder: trashed.Summary.Folder);

        // Same shape as trashing: rename first, rewrite second. A crash leaves a live
        // note that still carries a stale deletedAt, never a duplicate.
        string notePath = _storage.NotePath(id);
        VaultStorage.MoveVaultFile(trashPath, notePath);
        try
        {
            _storage.WriteText(notePath, text, trashText);
        }
        catch
        {
            VaultStorage.MoveVaultFile(notePath, trashPath);
            throw;
        }
    }

    internal Task PurgeAsync(Guid id, CancellationToken cancellationToken)
        => _storage.RunAsync(() => Purge(id, cancellationToken), VaultStorage.WriteTimeout, cancellationToken);

    private void Purge(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _storage.EnsureWritable();

        bool touched = false;

        string notePath = _storage.NotePath(id);
        if (File.Exists(notePath))
        {
            File.Delete(notePath);
            touched = true;
        }

        string trashPath = _storage.TrashPath(id);
        if (File.Exists(trashPath))
        {
            File.Delete(trashPath);
            touched = true;
        }

        if (_history.DeleteAll(id))
        {
            touched = true;
        }

        if (!touched)
        {
            throw new VaultNoteNotFoundException(id);
        }
    }

    internal Task<IReadOnlyList<TrashNoteSummary>> ListAsync(CancellationToken cancellationToken)
        => _storage.RunAsync(() => List(cancellationToken), VaultStorage.ReadTimeout, cancellationToken);

    private IReadOnlyList<TrashNoteSummary> List(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var items = new List<TrashNoteSummary>();
        if (!Directory.Exists(_storage.TrashDirectory))
        {
            return items;
        }

        foreach (string path in Directory.EnumerateFiles(
                     _storage.TrashDirectory,
                     "*.md",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string name = Path.GetFileNameWithoutExtension(path);
            if (!VaultStorage.TryParseCanonicalId(name, out Guid id))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Spec 12.4: one unavailable file must not fail the whole listing.
                DateTimeOffset stamp;
                try
                {
                    stamp = new DateTimeOffset(File.GetLastWriteTimeUtc(path));
                }
                catch (Exception)
                {
                    stamp = VaultStorage.UtcNowSeconds();
                }

                items.Add(new TrashNoteSummary(id, "(Unreadable note)", stamp, stamp, IsAvailable: false));
                continue;
            }

            string title;
            DateTimeOffset updated;

            try
            {
                NoteDocument document = VaultStorage.ReadDocument(id, VaultStorage.TrashRelativePath(id), text);
                title = document.Summary.Title;
                updated = document.Summary.Updated;
            }
            catch (VaultFormatException)
            {
                title = "(Broken note)";
                updated = new DateTimeOffset(File.GetLastWriteTimeUtc(path));
            }

            DateTimeOffset deletedAt = updated;
            if (TryReadFrontMatterValue(text, "deletedAt", out string? deletedText) &&
                DateTimeOffset.TryParse(
                    deletedText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset parsed))
            {
                deletedAt = parsed;
            }

            items.Add(new TrashNoteSummary(id, title, updated, deletedAt));
        }

        items.Sort(static (left, right) => right.DeletedAt.CompareTo(left.DeletedAt));
        return items;
    }

    internal Task<NoteDocument?> ReadAsync(Guid id, CancellationToken cancellationToken)
        => _storage.RunAsync(() => Read(id, cancellationToken), VaultStorage.ReadTimeout, cancellationToken);

    private NoteDocument? Read(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string path = _storage.TrashPath(id);
        if (!File.Exists(path))
        {
            return null;
        }

        return VaultStorage.ReadDocument(id, VaultStorage.TrashRelativePath(id), File.ReadAllText(path));
    }

    private static bool TryReadFrontMatterValue(string text, string key, out string? value)
    {
        value = null;
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return false;
        }

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Trim() == "---")
            {
                return false;
            }

            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            if (string.Equals(line[..colon].Trim(), key, StringComparison.Ordinal))
            {
                value = line[(colon + 1)..].Trim().Trim('"', '\'');
                return true;
            }
        }

        return false;
    }
}
