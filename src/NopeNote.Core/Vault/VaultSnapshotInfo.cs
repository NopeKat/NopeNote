namespace NopeNote.Core.Vault;

public sealed record VaultSnapshotInfo(
    string Name,
    string RelativePath,
    DateTimeOffset TakenAt);
