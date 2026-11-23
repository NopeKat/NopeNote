using NopeNote.Core.Notes;
using NopeNote.Core.Vault;
using Xunit;

namespace NopeNote.Core.Tests;

/// <summary>
/// Covers the degraded storage scenarios (no atomic rename, no advisory lock)
/// plus trash listing and the maintenance helpers: git init and legacy import.
/// </summary>
public sealed class VaultCapabilityTests
{
    [Fact]
    public async Task NonAtomicRename_StillWritesAndKeepsNoTempFiles()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = StoreWith(fixture, new VaultCapabilities(true, false, false, false));

        NoteDocument created = await store.CreateNoteAsync("无原子改名", "第一版");
        NoteDocument updated = await store.UpdateNoteAsync(created.Summary.Id, "第二版");

        Assert.Equal("第二版", updated.Body);
        Assert.NotNull(await store.ReadNoteAsync(created.Summary.Id));

        string[] leftovers = Directory
            .EnumerateFiles(fixture.Root, "*.tmp-*", SearchOption.AllDirectories)
            .ToArray();
        Assert.Empty(leftovers);
    }

    [Fact]
    public async Task NoAdvisoryLock_DoesNotBlockWrites()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = StoreWith(fixture, new VaultCapabilities(true, true, false, true));

        NoteDocument created = await store.CreateNoteAsync("无锁", "正文");
        await store.TrashNoteAsync(created.Summary.Id);

        Assert.Null(await store.ReadNoteAsync(created.Summary.Id));
        Assert.Single(await store.ListTrashAsync());
    }

    [Fact]
    public async Task CaseSensitivityFlag_IsInformationalOnly()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = StoreWith(fixture, new VaultCapabilities(true, true, false, false));

        NoteDocument created = await store.CreateNoteAsync("大小写", "正文");

        Assert.NotNull(await store.ReadNoteAsync(created.Summary.Id));
    }

    [Fact]
    public async Task TrashList_ExposesDeletedNotes()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = await OpenAsync(fixture);

        await store.TrashNoteAsync(VaultFixture.NoteId);

        IReadOnlyList<TrashNoteSummary> trash = await store.ListTrashAsync();
        TrashNoteSummary item = Assert.Single(trash);
        Assert.Equal(VaultFixture.NoteId, item.Id);
        Assert.Equal("Sample note", item.Title);
        Assert.True(item.DeletedAt >= item.Updated);
    }

    [Fact]
    public async Task TrashContent_StaysReadable()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = await OpenAsync(fixture);
        NoteDocument original = (await store.ReadNoteAsync(VaultFixture.NoteId))!;

        await store.TrashNoteAsync(VaultFixture.NoteId);

        NoteDocument? trashed = await store.ReadTrashAsync(VaultFixture.NoteId);
        Assert.NotNull(trashed);
        Assert.Equal(original.Body, trashed!.Body);
        Assert.Contains("deletedAt:", File.ReadAllText(
            Path.Combine(fixture.Root, ".trash", VaultFixture.NoteId.ToString("D") + ".md")));
    }

    [Fact]
    public async Task PurgeFromTrash_RemovesEverything()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = await OpenAsync(fixture);

        await store.TrashNoteAsync(VaultFixture.NoteId);
        await store.PurgeNoteAsync(VaultFixture.NoteId);

        Assert.Null(await store.ReadTrashAsync(VaultFixture.NoteId));
        Assert.Empty(await store.ListTrashAsync());
    }

    [Fact]
    public async Task TrashList_SortsByDeletedAtDescending()
    {
        using var fixture = new VaultFixture();
        fixture.Write($".trash/{VaultFixture.SecondNoteId:D}.md", TrashText(VaultFixture.SecondNoteId, "2026-11-21T09:00:00Z"));
        Guid third = Guid.Parse("44444444-4444-4444-8444-444444444444");
        fixture.Write($".trash/{third:D}.md", TrashText(third, "2026-11-23T09:00:00Z"));

        IVaultStore store = await OpenAsync(fixture);
        IReadOnlyList<TrashNoteSummary> trash = await store.ListTrashAsync();

        Assert.Equal(2, trash.Count);
        Assert.Equal(third, trash[0].Id);
        Assert.Equal(VaultFixture.SecondNoteId, trash[1].Id);
    }

    [Fact]
    public async Task GitInitialize_CreatesRepositoryAndFirstCommit()
    {
        using var fixture = new VaultFixture();

        bool initialized = await VaultGit.InitializeAsync(fixture.Root);
        if (!initialized)
        {
            return;
        }

        Assert.True(Directory.Exists(Path.Combine(fixture.Root, ".git")));
        Assert.True(File.Exists(Path.Combine(fixture.Root, ".git", "HEAD")));
    }

    [Fact]
    public async Task LegacyImport_TurnsTextFilesIntoNotes()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = await OpenAsync(fixture);

        string legacyDirectory = Path.Combine(fixture.Root, "legacy");
        Directory.CreateDirectory(legacyDirectory);
        File.WriteAllText(Path.Combine(legacyDirectory, "a.txt"), "第一行\r\n第二行\r\n");
        File.WriteAllText(Path.Combine(legacyDirectory, "b.txt"), "另一篇");

        int imported = await LegacyNotesImporter.ImportAsync(store, legacyDirectory);

        Assert.Equal(2, imported);

        NoteScanResult scan = await store.ScanNotesAsync();
        Assert.Equal(3, scan.Notes.Count);

        NoteDocument? importedNote = await store.ReadNoteAsync(
            scan.Notes.First(note => note.Title == "第一行").Id);
        Assert.NotNull(importedNote);
        Assert.Equal("第一行\n第二行\n", importedNote!.Body);
    }

    [Fact]
    public async Task LegacyImport_MissingDirectory_ImportsNothing()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = await OpenAsync(fixture);

        int imported = await LegacyNotesImporter.ImportAsync(
            store,
            Path.Combine(fixture.Root, "does-not-exist"));

        Assert.Equal(0, imported);
    }

    [Fact]
    public async Task CreateAsync_BuildsValidVault()
    {
        string root = Path.Combine(Path.GetTempPath(), "nopenote-new-" + Guid.NewGuid().ToString("N"));
        try
        {
            VaultOpenResult opened = await VaultStoreFactory.CreateAsync(root);

            Assert.Equal(VaultOpenStatus.Opened, opened.Status);
            Assert.NotNull(opened.Store);
            Assert.True(File.Exists(Path.Combine(root, ".nopenote")));

            VaultValidationReport report = await VaultValidator.ValidateAsync(opened.Store!);
            Assert.True(report.IsValid, string.Join("; ", report.Issues.Select(issue => issue.Code)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CreateAsync_RefusesDirectoryWithContent()
    {
        string root = Path.Combine(Path.GetTempPath(), "nopenote-full-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "keep.txt"), "content");

        try
        {
            await Assert.ThrowsAsync<VaultFormatException>(() => VaultStoreFactory.CreateAsync(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static IVaultStore StoreWith(VaultFixture fixture, VaultCapabilities capabilities)
        => new LocalVaultStore(fixture.Root, VaultMetadataReader.Read(fixture.Root), capabilities);

    private static async Task<IVaultStore> OpenAsync(VaultFixture fixture)
    {
        VaultOpenResult opened = await VaultStoreFactory.OpenAsync(fixture.Root);
        Assert.Equal(VaultOpenStatus.Opened, opened.Status);
        Assert.NotNull(opened.Store);
        return opened.Store!;
    }

    private static string TrashText(Guid id, string deletedAt)
        => $"""
        ---
        id: {id:D}
        created: 2026-11-21T09:00:00Z
        updated: 2026-11-21T09:30:00Z
        deletedAt: {deletedAt}
        title: Trashed
        pinned: false
        ---

        Body text.
        """;
}
