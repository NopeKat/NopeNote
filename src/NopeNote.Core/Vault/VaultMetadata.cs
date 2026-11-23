namespace NopeNote.Core.Vault;

/// <summary>Contents of the root `.nopenote` marker.</summary>
public sealed record VaultMetadata(
    string Schema,
    int FormatVersion,
    Guid RepoId,
    DateTimeOffset CreatedAt,
    bool Encrypted);
