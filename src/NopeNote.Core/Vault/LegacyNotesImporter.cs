namespace NopeNote.Core.Vault;

/// <summary>
/// One-time import of the development build's plain text notes
/// (%LOCALAPPDATA%\NopeNote\Notes\*.txt) into the vault.
/// </summary>
public static class LegacyNotesImporter
{
    public static async Task<int> ImportAsync(
        IVaultStore store,
        string sourceDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return 0;
        }

        int imported = 0;
        foreach (string path in Directory.EnumerateFiles(sourceDirectory, "*.txt", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string text = File.ReadAllText(path).Replace("\r\n", "\n").Replace('\r', '\n');
            DateTimeOffset created = ToUtc(File.GetCreationTimeUtc(path));
            DateTimeOffset updated = ToUtc(File.GetLastWriteTimeUtc(path));
            if (updated < created)
            {
                updated = created;
            }

            await store.CreateNoteAsync(
                string.Empty,
                text,
                createdAt: created,
                updatedAt: updated,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            imported++;
        }

        return imported;
    }

    private static DateTimeOffset ToUtc(DateTime value)
    {
        DateTime truncated = new(
            value.Year,
            value.Month,
            value.Day,
            value.Hour,
            value.Minute,
            value.Second,
            DateTimeKind.Utc);
        return new DateTimeOffset(truncated);
    }
}
