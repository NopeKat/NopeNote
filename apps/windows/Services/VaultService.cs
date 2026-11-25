using System;
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

    public string VaultPath => SettingsHelper.Current.VaultPath;

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

        VaultOpenResult result = await VaultStoreFactory.OpenAsync(path).ConfigureAwait(false);

        bool emptyOrMissing = await Task.Run(
            () => !Directory.Exists(path) || !Directory.EnumerateFileSystemEntries(path).Any())
            .ConfigureAwait(false);
        if (result.Status == VaultOpenStatus.NotAVault && emptyOrMissing)
        {
            result = await VaultStoreFactory.CreateAsync(path);
        }

        if (result.Status == VaultOpenStatus.Opened && result.Store is not null)
        {
            await EnsureGitAsync(path).ConfigureAwait(false);
            await EnsureLegacyImportAsync(result.Store, path).ConfigureAwait(false);

            VaultSettings settings = await Task.Run(
                () =>
                {
                    string settingsFile = Path.Combine(path, ".settings", "settings.json");
                    if (!File.Exists(settingsFile))
                    {
                        VaultSettings.Write(path, VaultSettings.Read(path));
                    }

                    return VaultSettings.Read(path);
                })
                .ConfigureAwait(false);

            if (settings.GitEnabled && Directory.Exists(Path.Combine(path, ".git")))
            {
                result = result with
                {
                    Store = new GitVaultStore(result.Store, new ProcessGitCommitter(path)),
                };
            }
        }

        OpenResult = result;
        return result;
    }

    private async Task EnsureGitAsync(string path)
    {
        if (_gitChecked || Directory.Exists(Path.Combine(path, ".git")))
        {
            return;
        }

        _gitChecked = true;
        await VaultGit.InitializeAsync(path);
    }

    private static async Task EnsureLegacyImportAsync(IVaultStore store, string vaultPath)
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

        if (imported > 0)
        {
            await VaultGit.CommitAsync(vaultPath, $"meta: import {imported} notes");
        }

        SettingsHelper.Current.LegacyImportDone = true;
        SettingsHelper.Current.Save();
    }
}
