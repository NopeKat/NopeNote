using System.Globalization;
using NopeNote.Core.Vault;

namespace NopeNote.Core.Notes;

/// <summary>
/// Minimal reader for the YAML subset allowed in a note: scalars, inline arrays
/// and single-level keys. Unknown keys are kept as raw lines.
/// </summary>
public static class FrontMatterParser
{
    public static NoteDocument Parse(Guid id, string relativePath, string text)
    {
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = normalized.Split('\n');

        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            throw new VaultFormatException($"{relativePath}: missing front matter fence.");
        }

        int end = -1;
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
            {
                end = i;
                break;
            }
        }

        if (end < 0)
        {
            throw new VaultFormatException($"{relativePath}: unterminated front matter.");
        }

        var raw = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 1; i < end; i++)
        {
            string line = lines[i];
            if (line.Length == 0 || line.StartsWith(' '))
            {
                throw new VaultFormatException($"{relativePath}: unsupported front matter line '{line}'.");
            }

            raw.Add(line);

            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            string key = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            values[key] = Unquote(value);
        }

        if (!values.TryGetValue("id", out string? idText) ||
            !Guid.TryParseExact(idText, "D", out Guid parsedId))
        {
            throw new VaultFormatException($"{relativePath}: missing or invalid id.");
        }

        if (parsedId != id)
        {
            throw new VaultFormatException($"{relativePath}: id does not match file name.");
        }

        DateTimeOffset created = ReadTimestamp(values, "created", relativePath);
        DateTimeOffset updated = ReadTimestamp(values, "updated", relativePath);
        if (updated < created)
        {
            throw new VaultFormatException($"{relativePath}: updated is earlier than created.");
        }

        var bodyLines = lines[(end + 1)..];
        string body = string.Join('\n', bodyLines);
        if (body.StartsWith('\n'))
        {
            body = body[1..];
        }

        string title = ResolveTitle(values, body);
        IReadOnlyList<string> tags = ReadTags(values);
        bool pinned = values.TryGetValue("pinned", out string? pinnedText) &&
                      string.Equals(pinnedText, "true", StringComparison.OrdinalIgnoreCase);
        string folder = values.TryGetValue("folder", out string? folderText) ? folderText.Trim() : string.Empty;

        var summary = new NoteSummary(id, title, created, updated, tags, pinned, folder, relativePath);
        return new NoteDocument(summary, body, raw, normalized);
    }

    private static DateTimeOffset ReadTimestamp(
        Dictionary<string, string> values,
        string key,
        string relativePath)
    {
        if (!values.TryGetValue(key, out string? text))
        {
            throw new VaultFormatException($"{relativePath}: missing {key}.");
        }

        if (!DateTimeOffset.TryParseExact(
                text,
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset value))
        {
            throw new VaultFormatException($"{relativePath}: {key} must be UTC ISO 8601 with seconds.");
        }

        return value;
    }

    private static string ResolveTitle(Dictionary<string, string> values, string body)
    {
        if (values.TryGetValue("title", out string? title) && !string.IsNullOrWhiteSpace(title))
        {
            return title.Trim();
        }

        foreach (string line in body.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                return trimmed[2..].Trim();
            }

            string plain = trimmed.TrimStart('-', '*', '>', '#', ' ');
            return plain.Length <= 80 ? plain : plain[..80];
        }

        return "(Untitled note)";
    }

    private static IReadOnlyList<string> ReadTags(Dictionary<string, string> values)
    {
        if (!values.TryGetValue("tags", out string? text))
        {
            return Array.Empty<string>();
        }

        string trimmed = text.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
        {
            trimmed = trimmed[1..^1];
        }

        return trimmed
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tag => tag.Trim().Trim('"', '\''))
            .Where(tag => tag.Length > 0)
            .ToArray();
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}
