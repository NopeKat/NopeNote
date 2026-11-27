namespace NopeNote.Core.Vault;

/// <summary>Contents of the root `.nopenote` marker.</summary>
public sealed record VaultMetadata(
    string Schema,
    int FormatVersion,
    Guid RepoId,
    DateTimeOffset CreatedAt,
    bool Encrypted)
{
    /// <summary>The repoId exactly as written in the marker, so case can be validated.</summary>
    public string RepoIdText { get; init; } = string.Empty;
}
