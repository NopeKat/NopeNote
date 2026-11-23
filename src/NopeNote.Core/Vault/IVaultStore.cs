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
        CancellationToken cancellationToken = default);

    Task<NoteDocument> UpdateNoteAsync(
        Guid id,
        string? body = null,
        string? title = null,
        IReadOnlyList<string>? tags = null,
        bool? pinned = null,
        DateTimeOffset? expectedUpdated = null,
        CancellationToken cancellationToken = default);

    Task TrashNoteAsync(Guid id, CancellationToken cancellationToken = default);

    Task RestoreNoteAsync(Guid id, CancellationToken cancellationToken = default);

    Task PurgeNoteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VaultSnapshotInfo>> ListHistoryAsync(Guid id, CancellationToken cancellationToken = default);

    Task<NoteDocument?> ReadHistoryAsync(
        Guid id,
        string snapshotName,
        CancellationToken cancellationToken = default);
}
