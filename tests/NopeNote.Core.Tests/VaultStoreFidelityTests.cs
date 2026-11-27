using NopeNote.Core.Notes;
using NopeNote.Core.Vault;
using Xunit;

namespace NopeNote.Core.Tests;

/// <summary>Behaviour the split note/trash/history stores must keep: conflict copy
/// naming, trash listing rules, purge reach and history filtering.</summary>
public sealed class VaultStoreFidelityTests
{
    [Fact]
    public async Task ConflictCopy_NamesFileWithDeviceId()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = OpenWithDeviceId(fixture, "abcd1234");
        NoteDocument current = (await store.ReadNoteAsync(VaultFixture.NoteId))!;

        VaultConflictException exception = await Assert.ThrowsAsync<VaultConflictException>(
            () => store.UpdateNoteAsync(
                VaultFixture.NoteId,
                "本地版本",
                expectedUpdated: current.Summary.Updated.AddSeconds(-1)));

        string name = Path.GetFileName(exception.ConflictPath);
        Assert.Matches(
            $"^{VaultFixture.NoteId:D}\\.abcd1234\\.\\d{{8}}T\\d{{6}}Z\\.md$",
            name);
    }

    [Fact]
    public async Task InvalidDeviceId_FallsBackToLocaldevInConflictCopy()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = OpenWithDeviceId(fixture, "BAD ID");
        NoteDocument current = (await store.ReadNoteAsync(VaultFixture.NoteId))!;

        VaultConflictException exception = await Assert.ThrowsAsync<VaultConflictException>(
            () => store.UpdateNoteAsync(
                VaultFixture.NoteId,
                "本地版本",
                expectedUpdated: current.Summary.Updated.AddSeconds(-1)));

        Assert.Contains(".localdev.", Path.GetFileName(exception.ConflictPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_ReportsNonCanonicalFileName()
    {
        using var fixture = new VaultFixture();
        fixture.Write(".vault/notes/not-a-guid.md", fixture.NoteText(Guid.NewGuid()));
        IVaultStore store = await OpenAsync(fixture);

        NoteScanResult scan = await store.ScanNotesAsync();

        Assert.Single(scan.Notes);
        Assert.Contains(
            scan.Issues,
            issue => issue.RelativePath == ".vault/notes/not-a-guid.md");
    }

    [Fact]
    public async Task ListTrash_ReturnsNewestDeletionFirst()
    {
        using var fixture = new VaultFixture();
        fixture.Write(
            $".trash/{VaultFixture.NoteId:D}.md",
            TrashText(VaultFixture.NoteId, "older", "2026-11-21T09:00:00Z"));
        fixture.Write(
            $".trash/{VaultFixture.SecondNoteId:D}.md",
            TrashText(VaultFixture.SecondNoteId, "newer", "2026-11-23T09:00:00Z"));
        IVaultStore store = await OpenAsync(fixture);

        IReadOnlyList<TrashNoteSummary> items = await store.ListTrashAsync();

        Assert.Equal(2, items.Count);
        Assert.Equal(VaultFixture.SecondNoteId, items[0].Id);
        Assert.Equal(VaultFixture.NoteId, items[1].Id);
    }

    [Fact]
    public async Task ListTrash_BrokenFileKeepsEntryWithFallbackTitle()
    {
        using var fixture = new VaultFixture();
        fixture.Write($".trash/{VaultFixture.SecondNoteId:D}.md", "not a note");
        IVaultStore store = await OpenAsync(fixture);

        IReadOnlyList<TrashNoteSummary> items = await store.ListTrashAsync();

        TrashNoteSummary item = Assert.Single(items);
        Assert.Equal(VaultFixture.SecondNoteId, item.Id);
        Assert.Equal("(Broken note)", item.Title);
    }

    [Fact]
    public async Task ListTrash_SkipsNonCanonicalFileNames()
    {
        using var fixture = new VaultFixture();
        fixture.Write(".trash/scratch.md", TrashText(Guid.NewGuid(), "scratch", "2026-11-21T09:00:00Z"));
        IVaultStore store = await OpenAsync(fixture);

        Assert.Empty(await store.ListTrashAsync());
    }

    [Fact]
    public async Task PurgeTrashedNote_RemovesTrashFileAndHistory()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = await OpenAsync(fixture);

        await store.UpdateNoteAsync(VaultFixture.NoteId, "触发一次快照");
        await store.TrashNoteAsync(VaultFixture.NoteId);
        await store.PurgeNoteAsync(VaultFixture.NoteId);

        Assert.Null(await store.ReadTrashAsync(VaultFixture.NoteId));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, ".history", VaultFixture.NoteId.ToString("D"))));
    }

    [Fact]
    public async Task TrashUnknownId_Throws()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = await OpenAsync(fixture);

        await Assert.ThrowsAsync<VaultNoteNotFoundException>(
            () => store.TrashNoteAsync(VaultFixture.SecondNoteId));
    }

    [Fact]
    public async Task RestoreUnknownId_Throws()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = await OpenAsync(fixture);

        await Assert.ThrowsAsync<VaultNoteNotFoundException>(
            () => store.RestoreNoteAsync(VaultFixture.SecondNoteId));
    }

    [Fact]
    public async Task ListHistory_IgnoresUnparsableSnapshotNames()
    {
        using var fixture = new VaultFixture();
        fixture.Write(
            $".history/{VaultFixture.NoteId:D}/notes.md",
            fixture.NoteText(VaultFixture.NoteId));
        IVaultStore store = await OpenAsync(fixture);

        Assert.Empty(await store.ListHistoryAsync(VaultFixture.NoteId));
    }

    [Fact]
    public async Task ReadHistory_MissingSnapshotReturnsNull()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = await OpenAsync(fixture);

        Assert.Null(await store.ReadHistoryAsync(VaultFixture.NoteId, "20260101T000000Z.md"));
    }

    [Fact]
    public async Task RegisterAttachment_CompletesWithoutWritingFiles()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = await OpenAsync(fixture);
        string attachments = Path.Combine(fixture.Root, ".vault", "atts");
        int before = Directory.Exists(attachments)
            ? Directory.EnumerateFiles(attachments).Count()
            : 0;

        await store.RegisterAttachmentAsync(new string('a', 64));

        int after = Directory.Exists(attachments)
            ? Directory.EnumerateFiles(attachments).Count()
            : 0;
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Scan_DerivesTitleFromBodyWhenFrontMatterHasNone()
    {
        using var fixture = new VaultFixture();
        fixture.Write(
            $".vault/notes/{VaultFixture.SecondNoteId:D}.md",
            $"""
            ---
            id: {VaultFixture.SecondNoteId:D}
            created: 2026-11-20T09:00:00Z
            updated: 2026-11-21T09:00:00Z
            tags: []
            pinned: false
            ---

            ## 正文里的标题

            {new string('正', 2000)}
            """);
        IVaultStore store = await OpenAsync(fixture);

        NoteScanResult scan = await store.ScanNotesAsync();

        NoteSummary note = Assert.Single(scan.Notes, item => item.Id == VaultFixture.SecondNoteId);
        Assert.Equal("正文里的标题", note.Title);
    }

    private static string TrashText(Guid id, string title, string deletedAt)
        => $"""
        ---
        id: {id:D}
        created: 2026-11-20T09:00:00Z
        updated: 2026-11-21T09:00:00Z
        title: {title}
        tags: []
        pinned: false
        deletedAt: {deletedAt}
        ---

        Body text.
        """;

    private static IVaultStore OpenWithDeviceId(VaultFixture fixture, string deviceId)
        => new LocalVaultStore(
            fixture.Root,
            VaultMetadataReader.Read(fixture.Root),
            new VaultCapabilities(Writable: true, AtomicRename: true, AdvisoryLock: false, CaseSensitive: true),
            deviceId);

    private static async Task<IVaultStore> OpenAsync(VaultFixture fixture)
    {
        VaultOpenResult opened = await VaultStoreFactory.OpenAsync(fixture.Root);
        Assert.Equal(VaultOpenStatus.Opened, opened.Status);
        Assert.NotNull(opened.Store);
        return opened.Store!;
    }
}
