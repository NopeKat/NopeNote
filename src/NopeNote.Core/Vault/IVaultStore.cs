using NopeNote.Core.Notes;

namespace NopeNote.Core.Vault;

public interface IVaultStore
{
    string RootPath { get; }

    VaultMetadata Metadata { get; }

    VaultCapabilities Capabilities { get; }

    Task<NoteScanResult> ScanNotesAsync(CancellationToken cancellationToken = default);

    Task<NoteDocument?> ReadNoteAsync(Guid id, CancellationToken cancellationToken = default);
}
