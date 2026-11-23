using NopeNote.Core.Notes;

namespace NopeNote.Core.Vault;

public sealed class LocalVaultStore : IVaultStore
{
    private readonly string _notesDirectory;

    public LocalVaultStore(string rootPath, VaultMetadata metadata, VaultCapabilities capabilities)
    {
        RootPath = rootPath;
        Metadata = metadata;
        Capabilities = capabilities;
        _notesDirectory = Path.Combine(rootPath, ".vault", "notes");
    }

    public string RootPath { get; }

    public VaultMetadata Metadata { get; }

    public VaultCapabilities Capabilities { get; }

    public Task<NoteScanResult> ScanNotesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var notes = new List<NoteSummary>();
        var issues = new List<VaultIssue>();

        if (!Directory.Exists(_notesDirectory))
        {
            return Task.FromResult(new NoteScanResult(notes, issues));
        }

        foreach (string path in Directory.EnumerateFiles(_notesDirectory, "*.md", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = $".vault/notes/{Path.GetFileName(path)}";

            if (!TryParseCanonicalId(Path.GetFileNameWithoutExtension(path), out Guid id))
            {
                issues.Add(new VaultIssue(relativePath, "file name is not a canonical UUID."));
                continue;
            }

            try
            {
                NoteDocument document = ReadDocument(id, relativePath, File.ReadAllText(path));
                notes.Add(document.Summary);
            }
            catch (VaultFormatException exception)
            {
                issues.Add(new VaultIssue(relativePath, exception.Message));
            }
            catch (IOException exception)
            {
                issues.Add(new VaultIssue(relativePath, exception.Message));
            }
        }

        notes.Sort(static (left, right) =>
        {
            int byUpdated = right.Updated.CompareTo(left.Updated);
            return byUpdated != 0 ? byUpdated : right.Id.CompareTo(left.Id);
        });

        return Task.FromResult(new NoteScanResult(notes, issues));
    }

    public Task<NoteDocument?> ReadNoteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string path = Path.Combine(_notesDirectory, id.ToString("D") + ".md");
        if (!File.Exists(path))
        {
            return Task.FromResult<NoteDocument?>(null);
        }

        string relativePath = $".vault/notes/{id:D}.md";
        return Task.FromResult<NoteDocument?>(ReadDocument(id, relativePath, File.ReadAllText(path)));
    }

    private static NoteDocument ReadDocument(Guid id, string relativePath, string text)
        => FrontMatterParser.Parse(id, relativePath, text);

    private static bool TryParseCanonicalId(string name, out Guid id)
    {
        id = Guid.Empty;
        if (!Guid.TryParseExact(name, "D", out Guid parsed))
        {
            return false;
        }

        string canonical = parsed.ToString("D");
        if (!string.Equals(name, canonical, StringComparison.Ordinal))
        {
            return false;
        }

        id = parsed;
        return true;
    }
}
