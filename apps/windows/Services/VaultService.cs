using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NopeNote.Core.Vault;
using NopeNote.Windows.Helpers;

namespace NopeNote.Windows.Services;

/// <summary>
/// Owns the vault for the process: opens the configured folder, creates it on
/// first run, and exposes the store plus the open result for the UI.
/// </summary>
public sealed class VaultService
{
    public static VaultService Current { get; } = new();

    private Task<VaultOpenResult>? _opening;

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

        OpenResult = result;
        return result;
    }
}
