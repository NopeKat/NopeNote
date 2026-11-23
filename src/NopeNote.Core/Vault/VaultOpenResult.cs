namespace NopeNote.Core.Vault;

public enum VaultOpenStatus
{
    Opened = 0,
    ReadOnly = 1,
    NotAVault = 2,
}

public sealed record VaultOpenResult(
    VaultOpenStatus Status,
    IVaultStore? Store,
    string Message)
{
    public bool IsOpened => Status == VaultOpenStatus.Opened;
}
