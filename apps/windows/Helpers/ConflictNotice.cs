using System;
using System.Diagnostics;
using System.IO;

namespace NopeNote.Windows.Helpers;

/// <summary>Resolves conflict-copy paths and reveals them in File Explorer.</summary>
internal static class ConflictNotice
{
    /// <summary>Places a store-relative conflict path under the vault root.</summary>
    internal static string ResolvePath(string conflictPath, string vaultRoot)
    {
        if (string.IsNullOrWhiteSpace(conflictPath))
        {
            return vaultRoot;
        }

        return Path.IsPathRooted(conflictPath)
            ? conflictPath
            : Path.GetFullPath(Path.Combine(vaultRoot, conflictPath));
    }

    /// <summary>Opens Explorer with the copy selected, or its folder when missing.</summary>
    internal static bool TryShowInExplorer(string fullPath)
    {
        try
        {
            if (File.Exists(fullPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{fullPath}\"",
                    UseShellExecute = true,
                });
                return true;
            }

            string? directory = Path.GetDirectoryName(fullPath);
            if (directory is not null && Directory.Exists(directory))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{directory}\"",
                    UseShellExecute = true,
                });
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }

        return false;
    }
}
