using NopeNote.Core.Vault;
using Xunit;

namespace NopeNote.Core.Tests;

/// <summary>
/// Runs the validator against the real sample vault when it can be located.
/// The sample lives outside the product repository, so the test is skipped
/// (by returning early) when the working copy is not present.
/// </summary>
public sealed class SampleVaultTests
{
    [Fact]
    public async Task SampleVault_IsValidAndReadable()
    {
        string? root = FindSampleVault();
        if (root is null)
        {
            return;
        }

        VaultOpenResult opened = await VaultStoreFactory.OpenAsync(root);
        Assert.Equal(VaultOpenStatus.Opened, opened.Status);
        Assert.NotNull(opened.Store);

        NoteScanResult scan = await opened.Store!.ScanNotesAsync();
        Assert.Equal(3, scan.Notes.Count);
        Assert.Empty(scan.Issues);

        VaultValidationReport report = await VaultValidator.ValidateAsync(opened.Store);
        Assert.True(report.IsValid, $"sample vault reported errors: {Describe(report)}");
    }

    private static string? FindSampleVault()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable("NOPENOTE_SAMPLE_VAULT");
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && Directory.Exists(fromEnvironment))
        {
            return fromEnvironment;
        }

        string? directory = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && directory is not null; depth++)
        {
            string candidate = Path.Combine(directory, "vault", "spec");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    private static string Describe(VaultValidationReport report)
        => string.Join(
            "; ",
            report.Issues.Select(issue => $"{issue.Code} {issue.RelativePath}"));
}
