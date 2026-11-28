using NopeNote.Core.Notes;

namespace NopeNote.Core.Vault;

/// <summary>Vault store backed by plain files. The work is split across
/// <see cref="NoteFileStore"/>, <see cref="TrashFileStore"/> and
/// <see cref="HistoryStore"/>, which share one <see cref="VaultStorage"/> gate.</summary>
public sealed class LocalVaultStore : IVaultStore
{
    private readonly NoteFileStore _notes;
    private readonly TrashFileStore _trash;
    private readonly HistoryStore _history;

    public LocalVaultStore(
        string rootPath,
        VaultMetadata metadata,
        VaultCapabilities capabilities,
        string? deviceId = "localdev")
    {
        RootPath = rootPath;
        Metadata = metadata;
        Capabilities = capabilities;

        var storage = new VaultStorage(rootPath, capabilities);
        storage.ClearTempDirectory();
        _history = new HistoryStore(storage);
        _notes = new NoteFileStore(storage, _history, NormalizeDeviceId(deviceId));
        _trash = new TrashFileStore(storage, _history);
    }

    public string RootPath { get; }

    public VaultMetadata Metadata { get; }

    public VaultCapabilities Capabilities { get; }

    /// <summary>Keeps only 8-character lowercase ids; anything else falls back.</summary>
    private static string NormalizeDeviceId(string? deviceId)
    {
        if (deviceId is null || deviceId.Length != 8)
        {
            return "localdev";
        }

        foreach (char character in deviceId)
        {
            bool digit = character >= '0' && character <= '9';
            bool lowerCase = character >= 'a' && character <= 'z';
            if (!digit && !lowerCase)
            {
                return "localdev";
            }
        }

        return deviceId;
    }

    public Task<NoteScanResult> ScanNotesAsync(CancellationToken cancellationToken = default)
        => _notes.ScanAsync(cancellationToken);

    public Task<NoteDocument?> ReadNoteAsync(Guid id, CancellationToken cancellationToken = default)
        => _notes.ReadAsync(id, cancellationToken);

    public Task<NoteDocument> CreateNoteAsync(
        string title,
        string body,
        IReadOnlyList<string>? tags = null,
        bool pinned = false,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        string? folder = null,
        CancellationToken cancellationToken = default)
        => _notes.CreateAsync(title, body, tags, pinned, createdAt, updatedAt, folder, cancellationToken);

    public Task<NoteDocument> UpdateNoteAsync(
        Guid id,
        string? body = null,
        string? title = null,
        IReadOnlyList<string>? tags = null,
        bool? pinned = null,
        DateTimeOffset? expectedUpdated = null,
        string? folder = null,
        CancellationToken cancellationToken = default,
        string? expectedHash = null)
        => _notes.UpdateAsync(id, body, title, tags, pinned, expectedUpdated, folder, cancellationToken, expectedHash);

    public Task TrashNoteAsync(Guid id, CancellationToken cancellationToken = default)
        => _trash.TrashAsync(id, cancellationToken);

    public Task RestoreNoteAsync(Guid id, CancellationToken cancellationToken = default)
        => _trash.RestoreAsync(id, cancellationToken);

    public Task PurgeNoteAsync(Guid id, CancellationToken cancellationToken = default)
        => _trash.PurgeAsync(id, cancellationToken);

    public Task<IReadOnlyList<TrashNoteSummary>> ListTrashAsync(CancellationToken cancellationToken = default)
        => _trash.ListAsync(cancellationToken);

    public Task<NoteDocument?> ReadTrashAsync(Guid id, CancellationToken cancellationToken = default)
        => _trash.ReadAsync(id, cancellationToken);

    public Task<IReadOnlyList<VaultSnapshotInfo>> ListHistoryAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => _history.ListAsync(id, cancellationToken);

    public Task<NoteDocument?> ReadHistoryAsync(
        Guid id,
        string snapshotName,
        CancellationToken cancellationToken = default)
        => _history.ReadAsync(id, snapshotName, cancellationToken);

    /// <summary>Records an attachment write; a git-backed store commits it as atts: add.</summary>
    public Task RegisterAttachmentAsync(string sha256, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RegisterAttachmentRemovalAsync(string name, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>Plain files are written synchronously inside the IO gate, so nothing is queued.</summary>
    public Task FlushAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
