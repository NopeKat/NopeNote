using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NopeNote.Core.Notes;

namespace NopeNote.Core.Vault;

/// <summary>
/// Read-only structural validation of a vault. Reports findings instead of throwing,
/// so the same pass can drive diagnostics in the UI and assert fixtures in tests.
/// </summary>
public static partial class VaultValidator
{
    private static readonly HashSet<string> RootMembers = new(StringComparer.Ordinal)
    {
        ".nopenote",
        ".vault",
        ".history",
        ".meta",
        ".trash",
        ".settings",
        ".sync",
        ".plugins",
        ".tmp",
        ".git",
        ".gitignore",
        ".gitattributes",
    };

    private static readonly string[] JunkNames =
    {
        ".DS_Store",
        "Thumbs.db",
        "desktop.ini",
    };

    public static Task<VaultValidationReport> ValidateAsync(
        IVaultStore store,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Validate(store, cancellationToken));

    public static VaultValidationReport Validate(
        IVaultStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        cancellationToken.ThrowIfCancellationRequested();

        var issues = new List<VaultValidationIssue>();
        string root = store.RootPath;

        try
        {
            ValidateMarker(store.Metadata, issues);
            ValidateStructure(root, issues);
            ValidateRootMembers(root, issues);
            ValidateJunkFiles(root, issues, cancellationToken);

            var noteTexts = new List<string>();
            var noteIds = ValidateNotes(root, issues, noteTexts, cancellationToken);
            var trashIds = ValidateTrash(root, issues, cancellationToken);

            foreach (Guid shared in noteIds.Intersect(trashIds))
            {
                issues.Add(new VaultValidationIssue(
                    "ID_IN_BOTH",
                    VaultIssueSeverity.Error,
                    $".vault/notes/{Canonical(shared)}.md",
                    "note exists both in notes and in trash."));
            }

            ValidateHistory(root, issues, noteIds, noteTexts, cancellationToken);
            ValidateAttachments(root, issues, noteTexts, cancellationToken);
            ValidateConflicts(root, issues, cancellationToken);
            ValidateSettings(root, issues, cancellationToken);
            ValidateGit(root, issues, cancellationToken);
        }
        catch (IOException exception)
        {
            issues.Add(new VaultValidationIssue("IO_ERROR", VaultIssueSeverity.Error, ".", exception.Message));
        }
        catch (UnauthorizedAccessException exception)
        {
            issues.Add(new VaultValidationIssue("IO_ERROR", VaultIssueSeverity.Error, ".", exception.Message));
        }

        var ordered = issues
            .OrderBy(issue => issue.RelativePath, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ToArray();

        return new VaultValidationReport(ordered);
    }

    private static void ValidateMarker(VaultMetadata metadata, List<VaultValidationIssue> issues)
    {
        if (!string.Equals(metadata.Schema, "nopenote.vault", StringComparison.Ordinal))
        {
            issues.Add(new VaultValidationIssue(
                "MARKER_SCHEMA",
                VaultIssueSeverity.Error,
                ".nopenote",
                $"schema must be 'nopenote.vault', found '{metadata.Schema}'."));
        }

        string repoIdText = string.IsNullOrEmpty(metadata.RepoIdText)
            ? metadata.RepoId.ToString("D")
            : metadata.RepoIdText;
        if (metadata.RepoId == Guid.Empty ||
            !IsCanonicalId(repoIdText) ||
            !string.Equals(repoIdText, repoIdText.ToLowerInvariant(), StringComparison.Ordinal))
        {
            issues.Add(new VaultValidationIssue(
                "MARKER_BAD_REPO_ID",
                VaultIssueSeverity.Error,
                ".nopenote",
                "repoId must be a lowercase UUID."));
        }

        if (metadata.FormatVersion < 1)
        {
            issues.Add(new VaultValidationIssue(
                "MARKER_BAD_FORMAT_VERSION",
                VaultIssueSeverity.Error,
                ".nopenote",
                "formatVersion must be a positive integer."));
        }

        if (metadata.Encrypted)
        {
            issues.Add(new VaultValidationIssue(
                "MARKER_ENCRYPTED",
                VaultIssueSeverity.Error,
                ".nopenote",
                "v1 cannot read encrypted vaults."));
        }
    }

    private static void ValidateStructure(string root, List<VaultValidationIssue> issues)
    {
        foreach (string directory in VaultLayout.RequiredDirectories)
        {
            if (!Directory.Exists(Path.Combine(root, directory)))
            {
                issues.Add(new VaultValidationIssue(
                    "MISSING_DIR",
                    VaultIssueSeverity.Error,
                    directory,
                    "required directory is missing."));
            }
        }
    }

    private static void ValidateRootMembers(string root, List<VaultValidationIssue> issues)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(root))
        {
            string name = Path.GetFileName(entry);
            if (!RootMembers.Contains(name))
            {
                issues.Add(new VaultValidationIssue(
                    "UNEXPECTED_ROOT_ENTRY",
                    VaultIssueSeverity.Warning,
                    name,
                    "entry is not part of the vault layout."));
            }
        }
    }

    private static void ValidateJunkFiles(
        string root,
        List<VaultValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        foreach (string path in EnumerateTree(root, skipTempAndGit: true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileName(path);

            bool junk = JunkNames.Any(junkName => string.Equals(name, junkName, StringComparison.OrdinalIgnoreCase))
                        || name.EndsWith(".Zone.Identifier", StringComparison.OrdinalIgnoreCase);

            if (junk)
            {
                issues.Add(new VaultValidationIssue(
                    "JUNK_FILE",
                    VaultIssueSeverity.Warning,
                    Relative(root, path),
                    "platform metadata file must not live in the vault."));
            }
        }
    }

    private static HashSet<Guid> ValidateNotes(
        string root,
        List<VaultValidationIssue> issues,
        List<string> noteTexts,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<Guid>();
        string notesDirectory = Path.Combine(root, ".vault", "notes");

        foreach (string path in SafeEnumerateFiles(notesDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileName(path);
            string relativePath = Relative(root, path);

            if (!name.EndsWith(".md", StringComparison.Ordinal) ||
                !IsCanonicalId(Path.GetFileNameWithoutExtension(name)))
            {
                issues.Add(new VaultValidationIssue(
                    "EXTERNAL_FILE",
                    VaultIssueSeverity.Warning,
                    relativePath,
                    "file name is not '<uuid>.md'; it is not parsed as a note."));
                continue;
            }

            string text = File.ReadAllText(path);
            noteTexts.Add(text);
            ValidateTextEncoding(relativePath, text, issues);

            Guid id = Guid.ParseExact(Path.GetFileNameWithoutExtension(name), "D");
            try
            {
                NoteDocument document = FrontMatterParser.Parse(id, relativePath, text);
                if (!ids.Add(document.Summary.Id))
                {
                    issues.Add(new VaultValidationIssue(
                        "DUPLICATE_ID",
                        VaultIssueSeverity.Error,
                        relativePath,
                        "another note uses the same id."));
                }
            }
            catch (VaultFormatException exception)
            {
                issues.Add(new VaultValidationIssue(
                    "NOTE_PARSE_ERROR",
                    VaultIssueSeverity.Error,
                    relativePath,
                    exception.Message));
            }
        }

        return ids;
    }

    private static HashSet<Guid> ValidateTrash(
        string root,
        List<VaultValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<Guid>();
        string trashDirectory = Path.Combine(root, ".trash");

        foreach (string path in SafeEnumerateFiles(trashDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileName(path);
            string relativePath = Relative(root, path);

            if (!name.EndsWith(".md", StringComparison.Ordinal) ||
                !IsCanonicalId(Path.GetFileNameWithoutExtension(name)))
            {
                issues.Add(new VaultValidationIssue(
                    "EXTERNAL_FILE",
                    VaultIssueSeverity.Warning,
                    relativePath,
                    "file name is not '<uuid>.md'; it is not parsed as a trashed note."));
                continue;
            }

            Guid id = Guid.ParseExact(Path.GetFileNameWithoutExtension(name), "D");
            ids.Add(id);

            string text = File.ReadAllText(path);
            ValidateTextEncoding(relativePath, text, issues);

            var frontMatter = ReadFrontMatter(text);
            if (frontMatter is null)
            {
                issues.Add(new VaultValidationIssue(
                    "NOTE_PARSE_ERROR",
                    VaultIssueSeverity.Error,
                    relativePath,
                    "missing front matter fence."));
                continue;
            }

            if (!frontMatter.TryGetValue("deletedAt", out string? deletedAt))
            {
                issues.Add(new VaultValidationIssue(
                    "TRASH_MISSING_DELETED_AT",
                    VaultIssueSeverity.Error,
                    relativePath,
                    "trashed note must carry deletedAt."));
            }
            else if (!IsTimestamp(deletedAt))
            {
                issues.Add(new VaultValidationIssue(
                    "TRASH_BAD_DELETED_AT",
                    VaultIssueSeverity.Error,
                    relativePath,
                    "deletedAt must be UTC ISO 8601 with seconds."));
            }

            if (frontMatter.TryGetValue("id", out string? idText) &&
                !string.Equals(idText, Canonical(id), StringComparison.Ordinal))
            {
                issues.Add(new VaultValidationIssue(
                    "TRASH_ID_MISMATCH",
                    VaultIssueSeverity.Error,
                    relativePath,
                    "front matter id does not match the file name."));
            }
        }

        return ids;
    }

    private static void ValidateHistory(
        string root,
        List<VaultValidationIssue> issues,
        HashSet<Guid> liveIds,
        List<string> texts,
        CancellationToken cancellationToken)
    {
        string historyDirectory = Path.Combine(root, ".history");
        if (!Directory.Exists(historyDirectory))
        {
            return;
        }

        foreach (string directory in Directory.EnumerateDirectories(historyDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileName(directory);
            string relativeDirectory = Relative(root, directory);

            if (!IsCanonicalId(name))
            {
                issues.Add(new VaultValidationIssue(
                    "HISTORY_BAD_DIR",
                    VaultIssueSeverity.Error,
                    relativeDirectory,
                    "history directory name must be a lowercase UUID."));
                continue;
            }

            Guid id = Guid.ParseExact(name, "D");
            if (!liveIds.Contains(id))
            {
                issues.Add(new VaultValidationIssue(
                    "HISTORY_ORPHAN_DIR",
                    VaultIssueSeverity.Info,
                    relativeDirectory,
                    "no live note uses this id; purging the note left history behind."));
            }

            foreach (string snapshot in SafeEnumerateFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fileName = Path.GetFileName(snapshot);
                string relativePath = Relative(root, snapshot);

                if (!IsSnapshotName(fileName))
                {
                    issues.Add(new VaultValidationIssue(
                        "HISTORY_BAD_SNAPSHOT",
                        VaultIssueSeverity.Error,
                        relativePath,
                        "snapshot name must be 'YYYYMMDDTHHMMSSZ.md'."));
                    continue;
                }

                string text = File.ReadAllText(snapshot);
                texts.Add(text);

                var frontMatter = ReadFrontMatter(text);
                if (frontMatter is not null &&
                    frontMatter.TryGetValue("id", out string? idText) &&
                    !string.Equals(idText, name, StringComparison.Ordinal))
                {
                    issues.Add(new VaultValidationIssue(
                        "HISTORY_ID_MISMATCH",
                        VaultIssueSeverity.Warning,
                        relativePath,
                        "snapshot front matter id does not match its directory."));
                }
            }
        }
    }

    private static void ValidateAttachments(
        string root,
        List<VaultValidationIssue> issues,
        List<string> texts,
        CancellationToken cancellationToken)
    {
        string attachmentDirectory = Path.Combine(root, VaultLayout.AttachmentsDirectory);
        var existing = new HashSet<string>(StringComparer.Ordinal);
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        foreach (string path in SafeEnumerateFiles(attachmentDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileName(path);
            string relativePath = Relative(root, path);

            if (!AttachmentNameRegex().IsMatch(name))
            {
                issues.Add(new VaultValidationIssue(
                    "ATT_BAD_NAME",
                    VaultIssueSeverity.Warning,
                    relativePath,
                    "attachment name must be '<sha256>.<ext>' with a lowercase extension."));
                continue;
            }

            existing.Add(name);

            string actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            if (!name.StartsWith(actual, StringComparison.Ordinal))
            {
                issues.Add(new VaultValidationIssue(
                    "ATT_HASH_MISMATCH",
                    VaultIssueSeverity.Error,
                    relativePath,
                    "file content does not match the hash in its name."));
            }
        }

        foreach (string text in texts)
        {
            foreach (Match match in AttachmentReferenceRegex().Matches(text))
            {
                referenced.Add(match.Groups["name"].Value);
            }
        }

        foreach (string name in referenced)
        {
            if (!existing.Contains(name))
            {
                issues.Add(new VaultValidationIssue(
                    "ATT_MISSING",
                    VaultIssueSeverity.Error,
                    $"{VaultLayout.AttachmentsDirectory}/{name}",
                    "attachment is referenced but not present."));
            }
        }

        foreach (string name in existing)
        {
            if (!referenced.Contains(name))
            {
                issues.Add(new VaultValidationIssue(
                    "ATT_ORPHAN",
                    VaultIssueSeverity.Info,
                    $"{VaultLayout.AttachmentsDirectory}/{name}",
                    "attachment is not referenced by any note or snapshot."));
            }
        }
    }

    private static void ValidateConflicts(
        string root,
        List<VaultValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        string conflictsDirectory = Path.Combine(root, ".sync", "conflicts");

        foreach (string path in SafeEnumerateFiles(conflictsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileName(path);

            if (!ConflictNameRegex().IsMatch(name))
            {
                issues.Add(new VaultValidationIssue(
                    "CONFLICT_BAD_NAME",
                    VaultIssueSeverity.Warning,
                    Relative(root, path),
                    "conflict copy name must be '<uuid>.<device8>.<YYYYMMDDTHHMMSSZ>.md'."));
            }
        }
    }

    private static void ValidateSettings(
        string root,
        List<VaultValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        string settingsDirectory = Path.Combine(root, ".settings");

        foreach (string path in SafeEnumerateFiles(settingsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!path.EndsWith(".json", StringComparison.Ordinal))
            {
                issues.Add(new VaultValidationIssue(
                    "SETTINGS_UNEXPECTED_FILE",
                    VaultIssueSeverity.Warning,
                    Relative(root, path),
                    "settings directory only holds small JSON documents."));
                continue;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    issues.Add(new VaultValidationIssue(
                        "SETTINGS_INVALID",
                        VaultIssueSeverity.Error,
                        Relative(root, path),
                        "settings file must contain a JSON object."));
                    continue;
                }

                if (document.RootElement.TryGetProperty("git.enabled", out JsonElement gitEnabled) &&
                    gitEnabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    issues.Add(new VaultValidationIssue(
                        "SETTINGS_INVALID",
                        VaultIssueSeverity.Error,
                        Relative(root, path),
                        "git.enabled must be a boolean."));
                }
            }
            catch (JsonException exception)
            {
                issues.Add(new VaultValidationIssue(
                    "SETTINGS_INVALID",
                    VaultIssueSeverity.Error,
                    Relative(root, path),
                    exception.Message));
            }
        }
    }

    private static void ValidateGit(
        string root,
        List<VaultValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(Path.Combine(root, ".git")))
        {
            return;
        }

        string ignorePath = Path.Combine(root, ".gitignore");
        if (!File.Exists(ignorePath))
        {
            issues.Add(new VaultValidationIssue(
                "GIT_IGNORE_MISSING",
                VaultIssueSeverity.Warning,
                ".gitignore",
                "git is enabled but .gitignore is missing; .history and .tmp would be tracked."));
            return;
        }

        string[] lines = File.ReadAllLines(ignorePath);
        bool ignoresHistory = lines.Any(line =>
        {
            string trimmed = line.Trim().TrimStart('/');
            return string.Equals(trimmed, ".history/", StringComparison.Ordinal) ||
                   string.Equals(trimmed, ".history", StringComparison.Ordinal);
        });

        if (!ignoresHistory)
        {
            issues.Add(new VaultValidationIssue(
                "GIT_HISTORY_NOT_IGNORED",
                VaultIssueSeverity.Warning,
                ".gitignore",
                ".history/ must stay out of git; history already lives in its own directory."));
        }
    }

    private static void ValidateTextEncoding(
        string relativePath,
        string text,
        List<VaultValidationIssue> issues)
    {
        if (text.Contains("\r\n", StringComparison.Ordinal) || text.Contains('\r'))
        {
            issues.Add(new VaultValidationIssue(
                "CRLF_LINE_ENDING",
                VaultIssueSeverity.Warning,
                relativePath,
                "vault text must use LF line endings."));
        }
    }

    private static Dictionary<string, string>? ReadFrontMatter(string text)
    {
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = normalized.Split('\n');

        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Trim() == "---")
            {
                return values;
            }

            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            values[line[..colon].Trim()] = line[(colon + 1)..].Trim().Trim('"', '\'');
        }

        return null;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly);
    }

    private static IEnumerable<string> EnumerateTree(string root, bool skipTempAndGit)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            string current = stack.Pop();
            foreach (string directory in Directory.EnumerateDirectories(current))
            {
                string name = Path.GetFileName(directory);
                if (skipTempAndGit && (string.Equals(name, ".tmp", StringComparison.Ordinal) ||
                                       string.Equals(name, ".git", StringComparison.Ordinal)))
                {
                    continue;
                }

                stack.Push(directory);
            }

            foreach (string file in Directory.EnumerateFiles(current))
            {
                yield return file;
            }
        }
    }

    private static string Relative(string root, string path)
        => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string Canonical(Guid id) => id.ToString("D");

    private static bool IsCanonicalId(string value)
        => UuidRegex().IsMatch(value) && string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal);

    private static bool IsSnapshotName(string name)
        => SnapshotNameRegex().IsMatch(name);

    private static bool IsTimestamp(string value)
        => DateTimeOffset.TryParseExact(
            value,
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out _);

    [GeneratedRegex("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex UuidRegex();

    [GeneratedRegex(@"^[0-9]{8}T[0-9]{6}Z\.md$", RegexOptions.CultureInvariant)]
    private static partial Regex SnapshotNameRegex();

    [GeneratedRegex(@"^[0-9a-f]{64}\.[a-z0-9]{1,12}$", RegexOptions.CultureInvariant)]
    private static partial Regex AttachmentNameRegex();

    [GeneratedRegex(@"atts/(?<name>[0-9a-f]{64}\.[a-z0-9]{1,12})", RegexOptions.CultureInvariant)]
    private static partial Regex AttachmentReferenceRegex();

    [GeneratedRegex(@"^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\.[0-9a-z]{8}\.[0-9]{8}T[0-9]{6}Z\.md$", RegexOptions.CultureInvariant)]
    private static partial Regex ConflictNameRegex();
}
