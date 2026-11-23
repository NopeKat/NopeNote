using NopeNote.Core.Vault;
using Xunit;

namespace NopeNote.Core.Tests;

public sealed class VaultValidatorTests
{
    [Fact]
    public async Task CleanVault_HasNoFindings()
    {
        using var fixture = new VaultFixture();

        VaultValidationReport report = await ValidateAsync(fixture);

        Assert.True(report.IsValid);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public async Task MissingRequiredDirectory_IsAnError()
    {
        using var fixture = new VaultFixture();
        fixture.Delete(".sync/conflicts");

        VaultValidationReport report = await ValidateAsync(fixture);

        AssertIssue(report, "MISSING_DIR", ".sync/conflicts", VaultIssueSeverity.Error);
        Assert.False(report.IsValid);
    }

    [Fact]
    public async Task FileWithoutCanonicalName_IsReportedAsExternal()
    {
        using var fixture = new VaultFixture();
        fixture.Write(".vault/notes/meeting-notes.md", fixture.NoteText(VaultFixture.NoteId));

        VaultValidationReport report = await ValidateAsync(fixture);

        AssertIssue(report, "EXTERNAL_FILE", ".vault/notes/meeting-notes.md", VaultIssueSeverity.Warning);
        Assert.True(report.IsValid);
    }

    [Fact]
    public async Task FrontMatterIdMustMatchFileName()
    {
        using var fixture = new VaultFixture();
        fixture.Write($".vault/notes/{VaultFixture.SecondNoteId:D}.md", fixture.NoteText(VaultFixture.NoteId));

        VaultValidationReport report = await ValidateAsync(fixture);

        AssertIssue(report, "NOTE_PARSE_ERROR", $".vault/notes/{VaultFixture.SecondNoteId:D}.md", VaultIssueSeverity.Error);
        Assert.False(report.IsValid);
    }

    [Fact]
    public async Task TrashedNoteMustCarryDeletedAt()
    {
        using var fixture = new VaultFixture();
        fixture.Write($".trash/{VaultFixture.SecondNoteId:D}.md", fixture.NoteText(VaultFixture.SecondNoteId));

        VaultValidationReport report = await ValidateAsync(fixture);

        AssertIssue(report, "TRASH_MISSING_DELETED_AT", $".trash/{VaultFixture.SecondNoteId:D}.md", VaultIssueSeverity.Error);
    }

    [Fact]
    public async Task NotePresentInBothNotesAndTrash_IsAnError()
    {
        using var fixture = new VaultFixture();
        fixture.Write($".trash/{VaultFixture.NoteId:D}.md", TrashedNoteText(VaultFixture.NoteId));

        VaultValidationReport report = await ValidateAsync(fixture);

        AssertIssue(report, "ID_IN_BOTH", $".vault/notes/{VaultFixture.NoteId:D}.md", VaultIssueSeverity.Error);
        Assert.False(report.IsValid);
    }

    [Fact]
    public async Task ValidTrashedNote_ProducesNoError()
    {
        using var fixture = new VaultFixture();
        fixture.Write($".trash/{VaultFixture.SecondNoteId:D}.md", TrashedNoteText(VaultFixture.SecondNoteId));

        VaultValidationReport report = await ValidateAsync(fixture);

        Assert.True(report.IsValid);
    }

    [Fact]
    public async Task AttachmentContentMustMatchItsHashName()
    {
        using var fixture = new VaultFixture();
        string name = new string('a', 64) + ".png";
        fixture.Write($".vault/atts/{name}", "not matching the hash");

        VaultValidationReport report = await ValidateAsync(fixture);

        AssertIssue(report, "ATT_HASH_MISMATCH", $".vault/atts/{name}", VaultIssueSeverity.Error);
        Assert.False(report.IsValid);
    }

    [Fact]
    public async Task ReferencedAttachmentMustExist()
    {
        using var fixture = new VaultFixture();
        string name = new string('b', 64) + ".png";
        fixture.Write(
            $".vault/notes/{VaultFixture.NoteId:D}.md",
            fixture.NoteText(VaultFixture.NoteId, body: $"![截图](../atts/{name})"));

        VaultValidationReport report = await ValidateAsync(fixture);

        AssertIssue(report, "ATT_MISSING", $".vault/atts/{name}", VaultIssueSeverity.Error);
    }

    [Fact]
    public async Task PlatformJunkFile_IsReported()
    {
        using var fixture = new VaultFixture();
        fixture.Write(".DS_Store", "junk");

        VaultValidationReport report = await ValidateAsync(fixture);

        AssertIssue(report, "JUNK_FILE", ".DS_Store", VaultIssueSeverity.Warning);
    }

    [Fact]
    public async Task SnapshotNameMustBeUtcTimestamp()
    {
        using var fixture = new VaultFixture();
        fixture.Write($".history/{VaultFixture.NoteId:D}/yesterday.md", fixture.NoteText(VaultFixture.NoteId));

        VaultValidationReport report = await ValidateAsync(fixture);

        AssertIssue(
            report,
            "HISTORY_BAD_SNAPSHOT",
            $".history/{VaultFixture.NoteId:D}/yesterday.md",
            VaultIssueSeverity.Error);
    }

    [Fact]
    public async Task ConflictCopyNameMustFollowPattern()
    {
        using var fixture = new VaultFixture();
        fixture.Write(".sync/conflicts/foo.md", fixture.NoteText(VaultFixture.NoteId));

        VaultValidationReport report = await ValidateAsync(fixture);

        AssertIssue(report, "CONFLICT_BAD_NAME", ".sync/conflicts/foo.md", VaultIssueSeverity.Warning);
    }

    [Fact]
    public async Task GitEnabledSettingMustBeBoolean()
    {
        using var fixture = new VaultFixture();
        fixture.Write(".settings/repo.json", """{ "git.enabled": "true" }""");

        VaultValidationReport report = await ValidateAsync(fixture);

        AssertIssue(report, "SETTINGS_INVALID", ".settings/repo.json", VaultIssueSeverity.Error);
    }

    [Fact]
    public async Task GitIgnoreMustKeepHistoryOutOfGit()
    {
        using var fixture = new VaultFixture();
        fixture.EnsureDirectory(".git");
        fixture.Write(".gitignore", ".tmp/\n*.tmp-*\n");

        VaultValidationReport report = await ValidateAsync(fixture);

        AssertIssue(report, "GIT_HISTORY_NOT_IGNORED", ".gitignore", VaultIssueSeverity.Warning);
    }

    [Fact]
    public async Task CrlfNote_IsReportedAsWarning()
    {
        using var fixture = new VaultFixture();
        string text = fixture.NoteText(VaultFixture.NoteId).Replace("\n", "\r\n");
        fixture.Write($".vault/notes/{VaultFixture.NoteId:D}.md", text);

        VaultValidationReport report = await ValidateAsync(fixture);

        AssertIssue(report, "CRLF_LINE_ENDING", $".vault/notes/{VaultFixture.NoteId:D}.md", VaultIssueSeverity.Warning);
    }

    private static string TrashedNoteText(Guid id)
        => $"""
        ---
        id: {id:D}
        created: 2026-11-21T09:00:00Z
        updated: 2026-11-21T09:10:00Z
        deletedAt: 2026-11-22T10:00:00Z
        title: Trashed note
        pinned: false
        ---

        Body text.
        """;

    private static async Task<VaultValidationReport> ValidateAsync(VaultFixture fixture)
    {
        VaultOpenResult opened = await VaultStoreFactory.OpenAsync(fixture.Root);
        Assert.NotNull(opened.Store);

        return await VaultValidator.ValidateAsync(opened.Store!);
    }

    private static void AssertIssue(
        VaultValidationReport report,
        string code,
        string relativePath,
        VaultIssueSeverity severity)
    {
        VaultValidationIssue[] matches = report.Issues
            .Where(candidate => candidate.Code == code)
            .ToArray();
        Assert.Single(matches);

        Assert.Equal(relativePath, matches[0].RelativePath);
        Assert.Equal(severity, matches[0].Severity);
    }
}
