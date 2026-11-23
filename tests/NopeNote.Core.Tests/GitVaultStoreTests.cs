using NopeNote.Core.Notes;
using NopeNote.Core.Vault;
using Xunit;

namespace NopeNote.Core.Tests;

public sealed class GitVaultStoreTests
{
    [Fact]
    public async Task CreateNote_CommitsWithCreateMessage()
    {
        using var fixture = new VaultFixture();
        var committer = new RecordingCommitter();
        IVaultStore store = Decorated(await OpenAsync(fixture), committer, TimeSpan.Zero);

        NoteDocument note = await store.CreateNoteAsync("t", "b");

        Assert.Equal($"note: create {note.Summary.Id:D}", Assert.Single(committer.Messages));
    }

    [Fact]
    public async Task UpdateWithoutDebounce_CommitsEachSave()
    {
        using var fixture = new VaultFixture();
        var committer = new RecordingCommitter();
        IVaultStore store = Decorated(await OpenAsync(fixture), committer, TimeSpan.Zero);

        await store.UpdateNoteAsync(VaultFixture.NoteId, "第一版");
        await store.UpdateNoteAsync(VaultFixture.NoteId, "第二版");

        Assert.Equal(2, committer.Messages.Count);
        Assert.All(committer.Messages, message => Assert.Equal($"note: update {VaultFixture.NoteId:D}", message));
    }

    [Fact]
    public async Task ContinuousEdits_ShareOneCommitInsideDebounceWindow()
    {
        using var fixture = new VaultFixture();
        var committer = new RecordingCommitter();
        var store = (GitVaultStore)Decorated(
            await OpenAsync(fixture),
            committer,
            TimeSpan.FromMilliseconds(150));

        await store.UpdateNoteAsync(VaultFixture.NoteId, "一");
        await store.UpdateNoteAsync(VaultFixture.NoteId, "二");
        await store.UpdateNoteAsync(VaultFixture.NoteId, "三");
        await store.FlushAsync();

        Assert.Equal($"note: update {VaultFixture.NoteId:D}", Assert.Single(committer.Messages));
    }

    [Fact]
    public async Task TrashRestoreAndPurge_CommitTheirOwnMessages()
    {
        using var fixture = new VaultFixture();
        var committer = new RecordingCommitter();
        IVaultStore store = Decorated(await OpenAsync(fixture), committer, TimeSpan.Zero);

        await store.TrashNoteAsync(VaultFixture.NoteId);
        await store.RestoreNoteAsync(VaultFixture.NoteId);
        await store.PurgeNoteAsync(VaultFixture.NoteId);

        Assert.Equal(
            new[]
            {
                $"note: trash {VaultFixture.NoteId:D}",
                $"note: restore {VaultFixture.NoteId:D}",
                $"note: purge {VaultFixture.NoteId:D}",
            },
            committer.Messages);
    }

    [Fact]
    public async Task FailedCommit_DoesNotBreakTheSave()
    {
        using var fixture = new VaultFixture();
        var committer = new RecordingCommitter { Fail = true };
        IVaultStore store = Decorated(await OpenAsync(fixture), committer, TimeSpan.Zero);

        NoteDocument note = await store.CreateNoteAsync("t", "正文保持不变");

        Assert.Contains("正文保持不变", note.Body);
        Assert.NotNull(await store.ReadNoteAsync(note.Summary.Id));
    }

    [Fact]
    public async Task NewVault_WritesGitEnabledSetting()
    {
        string root = Path.Combine(Path.GetTempPath(), "nopenote-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            await VaultStoreFactory.CreateAsync(root);

            string settingsPath = Path.Combine(root, ".settings", "settings.json");
            Assert.True(File.Exists(settingsPath));
            Assert.Contains("git.enabled", File.ReadAllText(settingsPath));
            Assert.True(VaultSettings.Read(root).GitEnabled);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void GitEnabledSetting_CanBeSwitchedOff()
    {
        string root = Path.Combine(Path.GetTempPath(), "nopenote-settings-off-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            VaultSettings.Write(root, new VaultSettings(GitEnabled: false));

            Assert.False(VaultSettings.Read(root).GitEnabled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static IVaultStore Decorated(IVaultStore inner, IVaultGitCommitter committer, TimeSpan debounce)
        => new GitVaultStore(inner, committer, debounce);

    private static async Task<IVaultStore> OpenAsync(VaultFixture fixture)
    {
        VaultOpenResult opened = await VaultStoreFactory.OpenAsync(fixture.Root);
        Assert.Equal(VaultOpenStatus.Opened, opened.Status);
        Assert.NotNull(opened.Store);
        return opened.Store!;
    }

    private sealed class RecordingCommitter : IVaultGitCommitter
    {
        public List<string> Messages { get; } = new();

        public bool Fail { get; init; }

        public Task<bool> CommitAsync(string message, CancellationToken cancellationToken = default)
        {
            if (Fail)
            {
                throw new IOException("commit failed");
            }

            Messages.Add(message);
            return Task.FromResult(true);
        }
    }
}
