using NopeNote.Core.Notes;

namespace NopeNote.Core.Vault;

/// <summary>
/// Decorates a store with git commits. Structural operations commit immediately;
/// continuous edits share one commit inside the debounce window so typing does
/// not spawn a git process per save.
/// </summary>
public sealed class GitVaultStore : IVaultStore
{
    private readonly IVaultStore _inner;
    private readonly IVaultGitCommitter _committer;
    private readonly TimeSpan _debounce;
    private readonly SemaphoreSlim _commitGate = new(1, 1);
    private readonly object _gate = new();

    private string? _pendingMessage;
    private CancellationTokenSource? _pendingCancellation;
    private Task? _pendingTask;

    public GitVaultStore(IVaultStore inner, IVaultGitCommitter committer, TimeSpan? debounce = null)
    {
        _inner = inner;
        _committer = committer;
        _debounce = debounce ?? TimeSpan.FromSeconds(60);
    }

    public string RootPath => _inner.RootPath;

    public VaultMetadata Metadata => _inner.Metadata;

    public VaultCapabilities Capabilities => _inner.Capabilities;

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
        CancellationToken cancellationToken = default)
    {
        NoteDocument document = await _inner.CreateNoteAsync(
            title, body, tags, pinned, createdAt, updatedAt, cancellationToken).ConfigureAwait(false);

        await CommitNowAsync($"note: create {document.Summary.Id:D}").ConfigureAwait(false);
        return document;
    }

    public async Task<NoteDocument> UpdateNoteAsync(
        Guid id,
        string? body = null,
        string? title = null,
        IReadOnlyList<string>? tags = null,
        bool? pinned = null,
        DateTimeOffset? expectedUpdated = null,
        CancellationToken cancellationToken = default)
    {
        NoteDocument document = await _inner.UpdateNoteAsync(
            id, body, title, tags, pinned, expectedUpdated, cancellationToken).ConfigureAwait(false);

        Schedule($"note: update {document.Summary.Id:D}");
        return document;
    }

    public async Task TrashNoteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _inner.TrashNoteAsync(id, cancellationToken).ConfigureAwait(false);
        await CommitNowAsync($"note: trash {id:D}").ConfigureAwait(false);
    }

    public async Task RestoreNoteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _inner.RestoreNoteAsync(id, cancellationToken).ConfigureAwait(false);
        await CommitNowAsync($"note: restore {id:D}").ConfigureAwait(false);
    }

    public async Task PurgeNoteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _inner.PurgeNoteAsync(id, cancellationToken).ConfigureAwait(false);
        await CommitNowAsync($"note: purge {id:D}").ConfigureAwait(false);
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

    /// <summary>
    /// Waits until the debounced commit (if any) has run. Tests use this instead
    /// of sleeping; the app does not need it because commits are best effort.
    /// </summary>
    public async Task FlushAsync()
    {
        while (true)
        {
            Task? pending;
            lock (_gate)
            {
                pending = _pendingTask;
            }

            if (pending is null)
            {
                break;
            }

            await pending.ConfigureAwait(false);

            lock (_gate)
            {
                if (ReferenceEquals(_pendingTask, pending))
                {
                    _pendingTask = null;
                }
            }
        }

        await _commitGate.WaitAsync().ConfigureAwait(false);
        _commitGate.Release();
    }

    private void Schedule(string message)
    {
        lock (_gate)
        {
            _pendingMessage = message;
            _pendingCancellation?.Cancel();

            var cancellation = new CancellationTokenSource();
            _pendingCancellation = cancellation;
            _pendingTask = RunScheduledCommitAsync(cancellation);
        }
    }

    private async Task RunScheduledCommitAsync(CancellationTokenSource cancellation)
    {
        try
        {
            if (_debounce > TimeSpan.Zero)
            {
                await Task.Delay(_debounce, cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        string? message;
        lock (_gate)
        {
            if (ReferenceEquals(_pendingCancellation, cancellation))
            {
                _pendingCancellation = null;
            }

            message = _pendingMessage;
            _pendingMessage = null;
        }

        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        await _commitGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _committer.CommitAsync(message).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A failed commit must never block a save; the spec only asks for a log entry.
        }
        finally
        {
            _commitGate.Release();
        }
    }

    private async Task CommitNowAsync(string message)
    {
        await _commitGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _committer.CommitAsync(message).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
        finally
        {
            _commitGate.Release();
        }
    }
}
