using NopeNote.Core.Notes;

namespace NopeNote.Core.Vault;

public interface IVaultStore
{
    string RootPath { get; }

    VaultMetadata Metadata { get; }

    VaultCapabilities Capabilities { get; }

    Task<NoteScanResult> ScanNotesAsync(CancellationToken cancellationToken = default);

    Task<NoteDocument?> ReadNoteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<NoteDocument> CreateNoteAsync(
        string title,
        string body,
        IReadOnlyList<string>? tags = null,
        bool pinned = false,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        string? folder = null,
        CancellationToken cancellationToken = default);

    Task<NoteDocument> UpdateNoteAsync(
        Guid id,
        string? body = null,
        string? title = null,
        IReadOnlyList<string>? tags = null,
        bool? pinned = null,
        DateTimeOffset? expectedUpdated = null,
        string? folder = null,
        CancellationToken cancellationToken = default);

    Task TrashNoteAsync(Guid id, CancellationToken cancellationToken = default);

    Task RestoreNoteAsync(Guid id, CancellationToken cancellationToken = default);

    Task PurgeNoteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrashNoteSummary>> ListTrashAsync(CancellationToken cancellationToken = default);

    Task<NoteDocument?> ReadTrashAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VaultSnapshotInfo>> ListHistoryAsync(Guid id, CancellationToken cancellationToken = default);

    Task<NoteDocument?> ReadHistoryAsync(
        Guid id,
        string snapshotName,
        CancellationToken cancellationToken = default);

    /// <summary>Records an attachment write; a git-backed store commits it as atts: add.</summary>
    Task RegisterAttachmentAsync(string sha256, CancellationToken cancellationToken = default);
}
