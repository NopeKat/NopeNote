using NopeNote.Core.Notes;

namespace NopeNote.Core.Vault;

public sealed record NoteScanResult(
    IReadOnlyList<NoteSummary> Notes,
    IReadOnlyList<VaultIssue> Issues);
