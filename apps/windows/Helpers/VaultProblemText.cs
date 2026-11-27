using NopeNote.Core.Vault;

namespace NopeNote.Windows.Helpers;

/// <summary>Turns a language-neutral <see cref="VaultProblem"/> into localized UI text.</summary>
internal static class VaultProblemText
{
    internal static string Describe(VaultOpenResult result)
    {
        return result.Problem switch
        {
            VaultProblem.DirectoryMissing => Strings.Get("VaultProblemDirectoryMissing"),
            VaultProblem.FormatInvalid => Strings.Format("VaultProblemFormatInvalid", result.Detail ?? string.Empty),
            VaultProblem.SchemaMismatch => Strings.Format("VaultProblemSchemaMismatch", result.Detail ?? string.Empty),
            VaultProblem.FormatVersionTooNew => Strings.Format("VaultProblemFormatVersionTooNew", result.Detail ?? string.Empty),
            VaultProblem.Encrypted => Strings.Get("VaultProblemEncrypted"),
            VaultProblem.ProbeFailed => Strings.Format("VaultProblemProbeFailed", result.Detail ?? string.Empty),
            VaultProblem.GitInvalid => Strings.Get("VaultProblemGitInvalid"),
            _ => Strings.Get("VaultProblemOpened"),
        };
    }
}
