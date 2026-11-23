using NopeNote.Core.Notes;
using NopeNote.Core.Vault;
using Xunit;

namespace NopeNote.Core.Tests;

public sealed class VaultWriteTests
{
    [Fact]
    public async Task CreateNote_WritesFileThatScanFinds()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = await OpenAsync(fixture);

        NoteDocument created = await store.CreateNoteAsync(
            "新笔记",
            "第一行\n第二行",
            new[] { "工作", "想法" },
            pinned: true);

        Assert.Equal("新笔记", created.Summary.Title);
        Assert.Equal(new[] { "工作", "想法" }, created.Summary.Tags);
        Assert.True(created.Summary.Pinned);
        Assert.True(File.Exists(Path.Combine(fixture.Root, ".vault", "notes", created.Summary.Id.ToString("D") + ".md")));

        NoteScanResult scan = await store.ScanNotesAsync();
        Assert.Equal(2, scan.Notes.Count);
        Assert.Contains(scan.Notes, note => note.Id == created.Summary.Id);
    }

    [Fact]
    public async Task CreateNote_UsesLowercaseUuidV4FileName()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = await OpenAsync(fixture);

        NoteDocument created = await store.CreateNoteAsync("t", "b");

        string name = Path.GetFileName(created.Summary.RelativePath);
        Assert.Equal(created.Summary.Id.ToString("D") + ".md", name);
        Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", name[..^3]);
    }

    [Fact]
    public async Task UpdateHelper_KeepsCreatedAndBumpsUpdated()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = await OpenAsync(fixture);

        NoteDocument before = (await store.ReadNoteAsync(VaultFixture.NoteId))!;
        NoteDocument after = await store.UpdateNoteAsync(VaultFixture.NoteId, "改写后的正文");

        Assert.Equal(before.Summary.Created, after.Summary.Created);
        Assert.True(after.Summary.Updated > before.Summary.Updated);
        Assert.Equal("改写后的正文", after.Body);
    }

    [Fact]
    public async Task Update_TakesSnapshotOfPreviousContent()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = await OpenAsync(fixture);
        string original = File.ReadAllText(Path.Combine(fixture.Root, ".vault", "notes", VaultFixture.NoteId.ToString("D") + ".md"));

        await store.UpdateNoteAsync(VaultFixture.NoteId, "新正文");

        IReadOnlyList<VaultSnapshotInfo> history = await store.ListHistoryAsync(VaultFixture.NoteId);
        Assert.Single(history);

        NoteDocument? snapshot = await store.ReadHistoryAsync(VaultFixture.NoteId, history[0].Name);
        Assert.NotNull(snapshot);
        Assert.Contains("Body text.", snapshot!.Body);

        string snapshotText = File.ReadAllText(Path.Combine(fixture.Root, ".history", VaultFixture.NoteId.ToString("D"), history[0].Name));
        Assert.Equal(original, snapshotText);
    }

    [Fact]
    public async Task SecondUpdateWithinThrottle_WritesNoAnotherSnapshot()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = await OpenAsync(fixture);

        await store.UpdateNoteAsync(VaultFixture.NoteId, "第一次");
        IReadOnlyList<VaultSnapshotInfo> afterFirst = await store.ListHistoryAsync(VaultFixture.NoteId);
        Assert.Single(afterFirst);

        await store.UpdateNoteAsync(VaultFixture.NoteId, "第二次");

        IReadOnlyList<VaultSnapshotInfo> afterSecond = await store.ListHistoryAsync(VaultFixture.NoteId);
        Assert.Single(afterSecond);
    }

    [Fact]
    public async Task Trash_AddsDeletedAtAndLeavesList()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = await OpenAsync(fixture);
        NoteDocument original = (await store.ReadNoteAsync(VaultFixture.NoteId))!;

        await store.TrashNoteAsync(VaultFixture.NoteId);

        Assert.Null(await store.ReadNoteAsync(VaultFixture.NoteId));
        NoteScanResult scan = await store.ScanNotesAsync();
        Assert.DoesNotContain(scan.Notes, note => note.Id == VaultFixture.NoteId);

        string trashText = File.ReadAllText(Path.Combine(fixture.Root, ".trash", VaultFixture.NoteId.ToString("D") + ".md"));
        Assert.Contains("deletedAt:", trashText);
        Assert.Contains(original.Body.Trim(), trashText);
    }

    [Fact]
    public async Task TrashThenRestore_ReturnsIdenticalBody()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = await OpenAsync(fixture);
        NoteDocument original = (await store.ReadNoteAsync(VaultFixture.NoteId))!;

        await store.TrashNoteAsync(VaultFixture.NoteId);
        await store.RestoreNoteAsync(VaultFixture.NoteId);

        NoteDocument restored = (await store.ReadNoteAsync(VaultFixture.NoteId))!;
        Assert.Equal(original.Body, restored.Body);
        Assert.False(restored.Summary.Updated < original.Summary.Updated);

        string noteText = File.ReadAllText(Path.Combine(fixture.Root, ".vault", "notes", VaultFixture.NoteId.ToString("D") + ".md"));
        Assert.DoesNotContain("deletedAt:", noteText);
        Assert.False(File.Exists(Path.Combine(fixture.Root, ".trash", VaultFixture.NoteId.ToString("D") + ".md")));
    }

    [Fact]
    public async Task Purge_RemovesNoteAndItsHistory()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = await OpenAsync(fixture);

        await store.UpdateNoteAsync(VaultFixture.NoteId, "触发一次快照");
        Assert.True(Directory.Exists(Path.Combine(fixture.Root, ".history", VaultFixture.NoteId.ToString("D"))));

        await store.PurgeNoteAsync(VaultFixture.NoteId);

        Assert.Null(await store.ReadNoteAsync(VaultFixture.NoteId));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, ".history", VaultFixture.NoteId.ToString("D"))));
    }

    [Fact]
    public async Task PurgeUnknownId_Throws()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = await OpenAsync(fixture);

        await Assert.ThrowsAsync<VaultNoteNotFoundException>(
            () => store.PurgeNoteAsync(VaultFixture.SecondNoteId));
    }

    [Fact]
    public async Task StaleExpectedUpdated_KeepsLocalVersionAsConflictCopy()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = await OpenAsync(fixture);
        NoteDocument current = (await store.ReadNoteAsync(VaultFixture.NoteId))!;

        VaultConflictException exception = await Assert.ThrowsAsync<VaultConflictException>(
            () => store.UpdateNoteAsync(
                VaultFixture.NoteId,
                "本地版本",
                expectedUpdated: current.Summary.Updated.AddSeconds(-1)));

        Assert.True(File.Exists(Path.Combine(fixture.Root, exception.ConflictPath.Replace('/', Path.DirectorySeparatorChar))));

        NoteDocument unchanged = (await store.ReadNoteAsync(VaultFixture.NoteId))!;
        Assert.Equal(current.Body, unchanged.Body);
    }

    [Fact]
    public async Task ReadOnlyVault_RejectsWrites()
    {
        using var fixture = new VaultFixture();
        fixture.Write(".nopenote", """
        {
          "schema": "nopenote.vault",
          "formatVersion": 2,
          "repoId": "33333333-3333-4333-8333-333333333333",
          "createdAt": "2026-11-21T08:00:00Z",
          "encryption": null,
          "features": {}
        }
        """);

        VaultOpenResult opened = await VaultStoreFactory.OpenAsync(fixture.Root);
        Assert.Equal(VaultOpenStatus.ReadOnly, opened.Status);
        Assert.NotNull(opened.Store);

        await Assert.ThrowsAsync<VaultReadOnlyException>(
            () => opened.Store!.CreateNoteAsync("t", "b"));
        await Assert.ThrowsAsync<VaultReadOnlyException>(
            () => opened.Store!.TrashNoteAsync(VaultFixture.NoteId));
    }

    [Fact]
    public async Task Update_WithoutBodyKeepsExistingText()
    {
        using var fixture = new VaultFixture();
        IVaultStore store = await OpenAsync(fixture);

        NoteDocument updated = await store.UpdateNoteAsync(VaultFixture.NoteId, title: "改标题");
        NoteDocument reloaded = (await store.ReadNoteAsync(VaultFixture.NoteId))!;

        Assert.Equal("改标题", updated.Summary.Title);
        Assert.Equal(reloaded.Body, updated.Body);
    }

    [Fact]
    public async Task Update_PreservesUnknownFrontMatterKeys()
    {
        using var fixture = new VaultFixture();
        string path = Path.Combine(fixture.Root, ".vault", "notes", VaultFixture.NoteId.ToString("D") + ".md");
        string text = File.ReadAllText(path).Replace("pinned: false", "pinned: false\ncolor: amber");
        File.WriteAllText(path, text);

        IVaultStore store = await OpenAsync(fixture);
        await store.UpdateNoteAsync(VaultFixture.NoteId, "新正文");

        string saved = File.ReadAllText(path);
        Assert.Contains("color: amber", saved);
    }

    private static async Task<IVaultStore> OpenAsync(VaultFixture fixture)
    {
        VaultOpenResult opened = await VaultStoreFactory.OpenAsync(fixture.Root);
        Assert.Equal(VaultOpenStatus.Opened, opened.Status);
        Assert.NotNull(opened.Store);
        return opened.Store!;
    }
}
