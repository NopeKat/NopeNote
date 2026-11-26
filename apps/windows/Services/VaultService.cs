using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NopeNote.Core.Vault;
using NopeNote.Windows.Helpers;

namespace NopeNote.Windows.Services;

/// <summary>
/// Owns the vault for the process: opens the configured folder, creates it on
/// first run, wires the optional git repository, imports legacy notes once, and
/// exposes the store plus the open result for the UI.
/// </summary>
public sealed class VaultService
{
    public static VaultService Current { get; } = new();

    private Task<VaultOpenResult>? _opening;
    private bool _gitChecked;

    public VaultOpenResult? OpenResult { get; private set; }

    public IVaultStore? Store => OpenResult?.Store;

    public bool IsWritable => Store?.Capabilities.Writable == true;

    /// <summary>每次笔记数据发生增删改后递增，缓存页面据此判断是否需要重新加载。</summary>
    public int DataVersion { get; private set; }

    /// <summary>Raised on the background pump when a queue git commit fails.</summary>
    public event Action<string>? CommitFailed;

    public void NotifyDataChanged()
    {
        DataVersion++;
    }

    public bool HasPendingCommits => Store is GitVaultStore git && git.HasPendingCommits;

    public string VaultPath => SettingsHelper.Current.VaultPath;

    /// <summary>Waits for queued git commits, bounded by <paramref name="timeout"/>.</summary>
    public async Task<bool> FlushCommitsAsync(TimeSpan timeout)
    {
        IVaultStore? store = Store;
        if (store is not GitVaultStore git)
        {
            return true;
        }

        try
        {
            await git.FlushAsync().WaitAsync(timeout).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is TimeoutException or IOException)
        {
            Debug.WriteLine($"nopenote: git flush failed: {exception.Message}");
            return false;
        }
    }

    public Task<VaultOpenResult> GetVaultAsync()
        => _opening ??= OpenAsync();

    public void Reset()
    {
        _opening = null;
        OpenResult = null;
        _gitChecked = false;
    }

    private async Task<VaultOpenResult> OpenAsync()
    {
        string path = VaultPath;

        VaultOpenResult result = await VaultStoreFactory.OpenAsync(
            path,
            SettingsHelper.Current.DeviceId).ConfigureAwait(false);

        bool emptyOrMissing = await Task.Run(
            () => !Directory.Exists(path) || !Directory.EnumerateFileSystemEntries(path).Any())
            .ConfigureAwait(false);
        if (result.Status == VaultOpenStatus.NotAVault && emptyOrMissing)
        {
            result = await VaultStoreFactory.CreateAsync(path, SettingsHelper.Current.DeviceId);
        }

        if (result.Status == VaultOpenStatus.Opened && result.Store is not null)
        {
            VaultSettings settings = await ReadVaultSettingsAsync(path).ConfigureAwait(false);

            if (settings.GitEnabled)
            {
                await EnsureGitAsync(path).ConfigureAwait(false);
                if (Directory.Exists(Path.Combine(path, ".git")))
                {
                    result = await WrapWithGitAsync(result, path).ConfigureAwait(false);
                }
            }

            await EnsureLegacyImportAsync(result.Store!, path, settings.GitEnabled).ConfigureAwait(false);
        }

        OpenResult = result;
        return result;
    }

    private async Task<VaultOpenResult> WrapWithGitAsync(VaultOpenResult result, string path)
    {
        IVaultStore? store = result.Store;
        if (store is null)
        {
            return result;
        }

        bool verified = await VaultGit.VerifyAsync(path).ConfigureAwait(false);
        if (!verified)
        {
            var readOnlyStore = new LocalVaultStore(
                path,
                store.Metadata,
                new VaultCapabilities(false, false, false, false),
                SettingsHelper.Current.DeviceId);
            return new VaultOpenResult(
                VaultOpenStatus.ReadOnly,
                readOnlyStore,
                "Git 仓库校验失败，已只读打开；请修复 .git 后重新打开。");
        }

        return result with
        {
            Store = new GitVaultStore(
                store,
                new ProcessGitCommitter(path),
                onCommitFailed: OnCommitFailed),
        };
    }

    private void OnCommitFailed(Exception exception)
    {
        Debug.WriteLine($"nopenote: git commit failed: {exception.Message}");
        CommitFailed?.Invoke(exception.Message);
    }

    private static Task<VaultSettings> ReadVaultSettingsAsync(string path)
        => Task.Run(
            () =>
            {
                string settingsFile = Path.Combine(path, ".settings", "settings.json");
                if (!File.Exists(settingsFile))
                {
                    VaultSettings.Write(path, VaultSettings.Read(path));
                }

                return VaultSettings.Read(path);
            });

    private async Task EnsureGitAsync(string path)
    {
        if (_gitChecked || Directory.Exists(Path.Combine(path, ".git")))
        {
            return;
        }

        _gitChecked = true;
        await VaultGit.InitializeAsync(path);
    }

    private static async Task EnsureLegacyImportAsync(IVaultStore store, string vaultPath, bool gitEnabled)
    {
        if (SettingsHelper.Current.LegacyImportDone)
        {
            return;
        }

        NoteScanResult scan = await store.ScanNotesAsync();
        if (scan.Notes.Count > 0)
        {
            return;
        }

        string legacyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NopeNote",
            "Notes");

        int imported;
        try
        {
            imported = await Task.Run(
                () => LegacyNotesImporter.ImportAsync(store, legacyDirectory))
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (imported > 0 && gitEnabled)
        {
            await VaultGit.CommitAsync(vaultPath, $"meta: import {imported} notes");
        }

        SettingsHelper.Current.LegacyImportDone = true;
        SettingsHelper.Current.Save();
    }
}
