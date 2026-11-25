using NopeNote.Core.Notes;

namespace NopeNote.Core.Vault;

/// <summary>
/// Decorates a store with git commits. Every commit is queued on a background
/// pump, so vault operations return without waiting for a git process.
/// Continuous edits share one commit inside the debounce window.
/// </summary>
public sealed class GitVaultStore : IVaultStore
{
    private readonly IVaultStore _inner;
    private readonly GitCommitQueue _commits;

    public GitVaultStore(
        IVaultStore inner,
        IVaultGitCommitter committer,
        TimeSpan? debounce = null,
        Action<Exception>? onCommitFailed = null)
    {
        _inner = inner;
        _commits = new GitCommitQueue(committer, debounce ?? TimeSpan.FromSeconds(60), onCommitFailed);
    }

    public string RootPath => _inner.RootPath;

    public VaultMetadata Metadata => _inner.Metadata;

    public VaultCapabilities Capabilities => _inner.Capabilities;

    /// <summary>True while any commit is queued, pending, or running.</summary>
    public bool HasPendingCommits => _commits.HasPendingCommits;

    public Task<NoteScanResult> ScanNotesAsync(CancellationToken cancellationToken = default)
        => _inner.ScanNotesAsync(cancellationToken);

    public Task<NoteDocument?> ReadNoteAsync(Guid id, CancellationToken cancellationToken = default)
        => _inner.ReadNoteAsync(id, cancellationToken);

    public async Task<NoteDocument> CreateNoteAsync(
        string title,
        string body,
        IReadOnlyList<string>? tags = null,
        bool pinned = false,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        string? folder = null,
        CancellationToken cancellationToken = default)
    {
        NoteDocument document = await _inner.CreateNoteAsync(
            title, body, tags, pinned, createdAt, updatedAt, folder, cancellationToken).ConfigureAwait(false);

        _commits.Enqueue($"note: create {document.Summary.Id:D}");
        return document;
    }

    public async Task<NoteDocument> UpdateNoteAsync(
        Guid id,
        string? body = null,
        string? title = null,
        IReadOnlyList<string>? tags = null,
        bool? pinned = null,
        DateTimeOffset? expectedUpdated = null,
        string? folder = null,
        CancellationToken cancellationToken = default)
    {
        NoteDocument document = await _inner.UpdateNoteAsync(
            id, body, title, tags, pinned, expectedUpdated, folder, cancellationToken).ConfigureAwait(false);

        _commits.ScheduleUpdate($"note: update {document.Summary.Id:D}");
        return document;
    }

    public async Task TrashNoteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _inner.TrashNoteAsync(id, cancellationToken).ConfigureAwait(false);
        _commits.Enqueue($"note: trash {id:D}");
    }

    public async Task RestoreNoteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _inner.RestoreNoteAsync(id, cancellationToken).ConfigureAwait(false);
        _commits.Enqueue($"note: restore {id:D}");
    }

    public async Task PurgeNoteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _inner.PurgeNoteAsync(id, cancellationToken).ConfigureAwait(false);
        _commits.Enqueue($"note: purge {id:D}");
    }

    public Task<IReadOnlyList<VaultSnapshotInfo>> ListHistoryAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => _inner.ListHistoryAsync(id, cancellationToken);

    public Task<NoteDocument?> ReadHistoryAsync(
        Guid id,
        string snapshotName,
        CancellationToken cancellationToken = default)
        => _inner.ReadHistoryAsync(id, snapshotName, cancellationToken);

    public Task<IReadOnlyList<TrashNoteSummary>> ListTrashAsync(CancellationToken cancellationToken = default)
        => _inner.ListTrashAsync(cancellationToken);

    public Task<NoteDocument?> ReadTrashAsync(Guid id, CancellationToken cancellationToken = default)
        => _inner.ReadTrashAsync(id, cancellationToken);

    /// <summary>Waits until every queued commit has been attempted.</summary>
    public Task FlushAsync(CancellationToken cancellationToken = default)
        => _commits.FlushAsync(cancellationToken);
}
