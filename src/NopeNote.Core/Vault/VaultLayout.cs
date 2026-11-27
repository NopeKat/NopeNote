namespace NopeNote.Core.Vault;

/// <summary>
/// Canonical vault layout shared by creation, validation and stores.
/// Paths are stored with forward slashes, as the spec requires.
/// </summary>
public static class VaultLayout
{
    public const string MarkerFileName = ".nopenote";

    public const string NotesDirectory = ".vault/notes";

    public const string AttachmentsDirectory = ".vault/atts";

    /// <summary>How attachments are linked from a note file, e.g. ../atts/&lt;sha256&gt;.png.</summary>
    public const string AttachmentReferencePrefix = "../atts";

    public const string HistoryDirectory = ".history";

    public const string TrashDirectory = ".trash";

    public const string ConflictsDirectory = ".sync/conflicts";

    public static IReadOnlyList<string> RequiredDirectories { get; } = new[]
    {
        ".vault",
        NotesDirectory,
        AttachmentsDirectory,
        ".vault/todos",
        HistoryDirectory,
        ".meta",
        TrashDirectory,
        ".settings",
        ".sync",
        ConflictsDirectory,
        ".plugins",
        ".tmp",
        ".tmp/.cache",
        ".tmp/.downloads",
    };

    public static string ToSystemPath(string rootPath, string relativePath)
        => Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
