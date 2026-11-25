using System.Globalization;
using System.Text;

namespace NopeNote.Core.Notes;

/// <summary>
/// Serializes a note in the shape required by the spec: YAML front matter in a
/// fixed key order, LF line endings, no BOM. Unknown front matter keys carried by
/// the previous file are appended unchanged so forward compatibility survives a save.
/// </summary>
public static class NoteFrontMatterWriter
{
    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "id",
        "created",
        "updated",
        "title",
        "tags",
        "folder",
        "pinned",
        "deletedAt",
    };

    public static string Build(
        Guid id,
        DateTimeOffset created,
        DateTimeOffset updated,
        string? title,
        IReadOnlyList<string> tags,
        bool pinned,
        string body,
        IReadOnlyList<string>? preservedLines = null,
        DateTimeOffset? deletedAt = null,
        string? folder = null)
    {
        var builder = new StringBuilder();
        builder.Append("---\n");
        builder.Append("id: ").Append(id.ToString("D")).Append('\n');
        builder.Append("created: ").Append(FormatTimestamp(created)).Append('\n');
        builder.Append("updated: ").Append(FormatTimestamp(updated)).Append('\n');

        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.Append("title: ").Append(FormatScalar(title.Trim())).Append('\n');
        }

        if (tags.Count > 0)
        {
            builder.Append("tags: [")
                .Append(string.Join(", ", tags.Select(FormatScalar)))
                .Append("]\n");
        }

        if (!string.IsNullOrWhiteSpace(folder))
        {
            builder.Append("folder: ").Append(FormatScalar(folder.Trim())).Append('\n');
        }

        builder.Append("pinned: ").Append(pinned ? "true" : "false").Append('\n');

        if (deletedAt.HasValue)
        {
            builder.Append("deletedAt: ").Append(FormatTimestamp(deletedAt.Value)).Append('\n');
        }

        if (preservedLines is not null)
        {
            foreach (string line in preservedLines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                int colon = line.IndexOf(':');
                string key = colon > 0 ? line[..colon].Trim() : string.Empty;
                if (KnownKeys.Contains(key))
                {
                    continue;
                }

                builder.Append(line.TrimEnd()).Append('\n');
            }
        }

        builder.Append("---\n");
        builder.Append('\n');
        builder.Append(NormalizeBody(body));

        return builder.ToString();
    }

    public static string NormalizeBody(string body)
        => body.Replace("\r\n", "\n").Replace('\r', '\n');

    public static string FormatTimestamp(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string FormatScalar(string value)
    {
        bool needsQuotes = value.Contains(':') ||
                           value.Contains('#') ||
                           value.Contains('[') ||
                           value.Contains(']') ||
                           value.Contains(',') ||
                           value.Contains('"') ||
                           value.StartsWith('-') ||
                           value.StartsWith(' ');

        if (!needsQuotes)
        {
            return value;
        }

        return '"' + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + '"';
    }
}
