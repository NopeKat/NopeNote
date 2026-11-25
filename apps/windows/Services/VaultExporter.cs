using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace NopeNote.Windows.Services;

/// <summary>Packs the vault into a zip, skipping git internals and temp files.</summary>
public static class VaultExporter
{
    private static readonly string[] SkippedRootDirectories = { ".git", ".tmp" };

    public static Task ExportAsync(string vaultPath, string destinationPath)
        => Task.Run(() => Export(vaultPath, destinationPath));

    private static void Export(string vaultPath, string destinationPath)
    {
        string vaultRoot = Path.GetFullPath(vaultPath);
        string destination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        using FileStream stream = new(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        using ZipArchive archive = new(stream, ZipArchiveMode.Create);

        foreach (string file in Directory.EnumerateFiles(vaultRoot, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(file, destination, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(vaultRoot, file);
            string rootSegment = relativePath.Split(Path.DirectorySeparatorChar)[0];
            if (Array.IndexOf(SkippedRootDirectories, rootSegment) >= 0)
            {
                continue;
            }

            if (Path.GetFileName(file).Contains(".tmp-", StringComparison.Ordinal))
            {
                continue;
            }

            ZipArchiveEntry entry = archive.CreateEntry(
                relativePath.Replace(Path.DirectorySeparatorChar, '/'),
                CompressionLevel.Optimal);

            using Stream entryStream = entry.Open();
            using FileStream source = File.OpenRead(file);
            source.CopyTo(entryStream);
        }
    }
}
