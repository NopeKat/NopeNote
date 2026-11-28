using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;

namespace NopeNote.Core.Vault;

/// <summary>An attachment file that no note, snapshot or template points at.</summary>
public sealed record AttachmentOrphan(string Name, long Size);

/// <summary>Result of a read-only scan for unreferenced attachments.</summary>
public sealed record AttachmentGcPlan(IReadOnlyList<AttachmentOrphan> Orphans, int ReferencedCount)
{
    public int Count => Orphans.Count;

    public long Bytes
    {
        get
        {
            long total = 0;
            foreach (AttachmentOrphan orphan in Orphans)
            {
                total += orphan.Size;
            }

            return total;
        }
    }
}

/// <summary>
/// Finds attachment files that nothing points at and moves them into the cache
/// quarantine. References are collected from notes, trash, history, conflicts and
/// templates, so an attachment that only the trash still uses is kept. Nothing is
/// deleted until the caller explicitly purges the quarantine.
/// </summary>
public static partial class AttachmentGc
{
    public const string QuarantineRelativePath = ".tmp/.cache/att-gc";

    private static readonly string[] TextExtensions = { ".md", ".json", ".txt" };

    private static readonly string[] SkippedDirectories = { ".git", ".tmp", "bin", "obj" };

    public static AttachmentGcPlan Scan(IVaultStore store, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        return ScanRoot(store.RootPath, cancellationToken);
    }

    public static int Quarantine(
        IVaultStore store,
        AttachmentGcPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        RequireWritable(store);
        return QuarantineRoot(store.RootPath, plan, cancellationToken);
    }

    public static int PurgeQuarantine(IVaultStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        RequireWritable(store);
        return PurgeRoot(store.RootPath);
    }

    public static bool HasQuarantine(IVaultStore store)
        => Directory.Exists(Path.Combine(store.RootPath, QuarantineRelativePath));

    internal static AttachmentGcPlan ScanRoot(string root, CancellationToken cancellationToken)
    {
        string attachmentDirectory = Path.Combine(root, VaultLayout.AttachmentsDirectory);
        var existing = new List<AttachmentOrphan>();
        if (Directory.Exists(attachmentDirectory))
        {
            foreach (string path in Directory.EnumerateFiles(attachmentDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = Path.GetFileName(path);
                if (!AttachmentNameRegex().IsMatch(name))
                {
                    continue;
                }

                existing.Add(new AttachmentOrphan(name, new FileInfo(path).Length));
            }
        }

        HashSet<string> referenced = CollectReferences(root, cancellationToken);
        var orphans = new List<AttachmentOrphan>();
        int referencedCount = 0;
        foreach (AttachmentOrphan file in existing)
        {
            if (referenced.Contains(file.Name))
            {
                referencedCount++;
            }
            else
            {
                orphans.Add(file);
            }
        }

        orphans.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
        return new AttachmentGcPlan(orphans, referencedCount);
    }

    internal static int QuarantineRoot(
        string root,
        AttachmentGcPlan plan,
        CancellationToken cancellationToken)
    {
        if (plan.Count == 0)
        {
            return 0;
        }

        string source = Path.Combine(root, VaultLayout.AttachmentsDirectory);
        string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        string destination = Path.Combine(root, QuarantineRelativePath, stamp);
        Directory.CreateDirectory(destination);

        var movedNames = new List<string>();
        foreach (AttachmentOrphan orphan in plan.Orphans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string from = Path.Combine(source, orphan.Name);
            if (!File.Exists(from))
            {
                continue;
            }

            File.Move(from, Path.Combine(destination, orphan.Name));
            movedNames.Add(orphan.Name);
        }

        string manifest = JsonSerializer.Serialize(
            new
            {
                created = DateTimeOffset.UtcNow,
                files = movedNames,
            },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(destination, "manifest.json"), manifest);
        return movedNames.Count;
    }

    internal static int PurgeRoot(string root)
    {
        string directory = Path.Combine(root, QuarantineRelativePath);
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        int deleted = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Count();
        Directory.Delete(directory, recursive: true);
        return deleted;
    }

    private static HashSet<string> CollectReferences(string root, CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCandidate(path, root))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (Match match in AttachmentReferenceRegex().Matches(text))
            {
                names.Add(match.Groups["name"].Value);
            }
        }

        return names;
    }

    private static bool IsCandidate(string path, string root)
    {
        if (!TextExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        string relative = Path.GetRelativePath(root, path);
        string[] parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Length < 2)
        {
            return true;
        }

        foreach (string part in parts[..^1])
        {
            if (SkippedDirectories.Contains(part, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return !relative.StartsWith(VaultLayout.AttachmentsDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static void RequireWritable(IVaultStore store)
    {
        if (!store.Capabilities.Writable)
        {
            throw new VaultReadOnlyException("vault was opened read-only; attachments cannot be reclaimed.");
        }
    }

    [GeneratedRegex(@"atts/(?<name>[0-9a-f]{64}\.[a-z0-9]{1,12})", RegexOptions.CultureInvariant)]
    private static partial Regex AttachmentReferenceRegex();

    [GeneratedRegex(@"^[0-9a-f]{64}\.[a-z0-9]{1,12}$", RegexOptions.CultureInvariant)]
    private static partial Regex AttachmentNameRegex();
}
