namespace NopeNote.Core.Vault;

/// <summary>
/// Best-effort capability probe. Runs inside the vault temp directory and never
/// leaves files behind. A failed rename or lock only lowers the capability flags;
/// only a failed write/read marks the vault as not writable.
/// </summary>
public static class VaultProbe
{
    public static VaultCapabilities Probe(string rootPath)
    {
        string tempDirectory = Path.Combine(rootPath, ".tmp");
        Directory.CreateDirectory(tempDirectory);

        string probePath = Path.Combine(tempDirectory, $"probe-{Guid.NewGuid():N}.tmp");
        string renamedPath = probePath + ".renamed";
        string lockPath = Path.Combine(tempDirectory, $"lock-{Guid.NewGuid():N}.tmp");
        string casePath = Path.Combine(tempDirectory, $"case-{Guid.NewGuid():N}.tmp");

        bool atomicRename = false;
        bool advisoryLock = false;
        bool caseSensitive = false;

        try
        {
            File.WriteAllText(probePath, "nopenote-probe");
            if (File.ReadAllText(probePath) != "nopenote-probe")
            {
                throw new IOException("probe read back did not match.");
            }

            try
            {
                File.Move(probePath, renamedPath);
                atomicRename = File.Exists(renamedPath) && !File.Exists(probePath);
            }
            catch (IOException)
            {
                atomicRename = false;
            }

            string verifyPath = atomicRename ? renamedPath : probePath;
            if (File.ReadAllText(verifyPath) != "nopenote-probe")
            {
                throw new IOException("probe content changed after rename.");
            }

            try
            {
                using (new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    advisoryLock = true;
                }
            }
            catch (IOException)
            {
                advisoryLock = false;
            }

            try
            {
                File.WriteAllText(casePath, "case");
                caseSensitive = !File.Exists(casePath.ToUpperInvariant());
            }
            catch (IOException)
            {
                caseSensitive = false;
            }
        }
        finally
        {
            TryDelete(probePath);
            TryDelete(renamedPath);
            TryDelete(lockPath);
            TryDelete(casePath);
        }

        return new VaultCapabilities(true, atomicRename, advisoryLock, caseSensitive);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 探测残留的清理是尽力而为：文件仍被占用时留给下次覆盖，
            // 不影响本次探测结论，所以不向上传递。
        }
    }
}
