namespace NopeNote.Core.Vault;

public sealed class VaultReadOnlyException : Exception
{
    public VaultReadOnlyException(string message) : base(message)
    {
    }
}

public sealed class VaultNoteNotFoundException : Exception
{
    public VaultNoteNotFoundException(Guid id)
        : base($"note {id:D} does not exist.")
    {
    }
}

public sealed class VaultConflictException : Exception
{
    public VaultConflictException(string conflictPath)
        : base($"the note changed on disk; local content was kept at {conflictPath}.")
    {
        ConflictPath = conflictPath;
    }

    public string ConflictPath { get; }
}
