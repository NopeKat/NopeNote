namespace NopeNote.Core.Vault;

public enum VaultOpenStatus
{
    Opened = 0,
    ReadOnly = 1,
    NotAVault = 2,
}

/// <summary>
/// Language-neutral reason a vault could not be opened, or was opened read-only.
/// The UI layer turns this into localized text; Core never returns user-facing prose.
/// </summary>
public enum VaultProblem
{
    None = 0,
    DirectoryMissing = 1,
    FormatInvalid = 2,
    SchemaMismatch = 3,
    FormatVersionTooNew = 4,
    Encrypted = 5,
    ProbeFailed = 6,
    GitInvalid = 7,
}

public sealed record VaultOpenResult(
    VaultOpenStatus Status,
    IVaultStore? Store,
    VaultProblem Problem = VaultProblem.None,
    string? Detail = null)
{
    public bool IsOpened => Status == VaultOpenStatus.Opened;
}
