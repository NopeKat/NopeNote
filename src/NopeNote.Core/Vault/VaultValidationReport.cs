namespace NopeNote.Core.Vault;

public enum VaultIssueSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record VaultValidationIssue(
    string Code,
    VaultIssueSeverity Severity,
    string RelativePath,
    string Message);

public sealed record VaultValidationReport(IReadOnlyList<VaultValidationIssue> Issues)
{
    public static VaultValidationReport Empty { get; } = new(Array.Empty<VaultValidationIssue>());

    public int ErrorCount => Issues.Count(issue => issue.Severity == VaultIssueSeverity.Error);

    public int WarningCount => Issues.Count(issue => issue.Severity == VaultIssueSeverity.Warning);

    public bool IsValid => ErrorCount == 0;

    public bool Contains(string code)
        => Issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal));
}
