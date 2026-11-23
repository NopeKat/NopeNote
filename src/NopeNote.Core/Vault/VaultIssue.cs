namespace NopeNote.Core.Vault;

/// <summary>A single file the scanner could not accept, with the reason.</summary>
public sealed record VaultIssue(string RelativePath, string Message);
