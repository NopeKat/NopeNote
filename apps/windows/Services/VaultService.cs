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

        VaultOpenResult result = await VaultStoreFactory.OpenAsync(path);

        bool emptyOrMissing = !Directory.Exists(path) || !Directory.EnumerateFileSystemEntries(path).Any();
        if (result.Status == VaultOpenStatus.NotAVault && emptyOrMissing)
        {
            result = await VaultStoreFactory.CreateAsync(path);
        }

        if (result.Status == VaultOpenStatus.Opened && result.Store is not null)
        {
            await EnsureGitAsync(path);
            await EnsureLegacyImportAsync(result.Store);
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

    private static async Task EnsureLegacyImportAsync(IVaultStore store)
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

        try
        {
            await LegacyNotesImporter.ImportAsync(store, legacyDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        SettingsHelper.Current.LegacyImportDone = true;
        SettingsHelper.Current.Save();
    }
}
