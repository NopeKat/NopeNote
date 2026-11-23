namespace NopeNote.Core.Vault;

/// <summary>Capabilities detected once when a vault is opened.</summary>
public sealed record VaultCapabilities(
    bool Writable,
    bool AtomicRename,
    bool AdvisoryLock,
    bool CaseSensitive);
