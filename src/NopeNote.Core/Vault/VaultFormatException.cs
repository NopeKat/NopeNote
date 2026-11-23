namespace NopeNote.Core.Vault;

/// <summary>Raised when a file inside the vault violates the format rules.</summary>
public sealed class VaultFormatException : Exception
{
    public VaultFormatException(string message)
        : base(message)
    {
    }

    public VaultFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
