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

public sealed class VaultTimeoutException : Exception
{
    public VaultTimeoutException(TimeSpan timeout, bool mayHaveCompleted = false)
        : base($"vault operation timed out after {timeout.TotalSeconds:0} seconds.")
    {
        MayHaveCompleted = mayHaveCompleted;
    }

    /// <summary>True when the abandoned action may still finish after the caller
    /// already received this exception (for example a write that outlived its wait).</summary>
    public bool MayHaveCompleted { get; }
}
