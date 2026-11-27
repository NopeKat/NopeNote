namespace NopeNote.Core.Notes;

/// <summary>A note read in full, keeping the original front matter lines.</summary>
public sealed record NoteDocument(
    NoteSummary Summary,
    string Body,
    IReadOnlyList<string> FrontMatterLines,
    string RawText)
{
    /// <summary>SHA-256 of <see cref="RawText"/>, lowercase hex; empty for callers
    /// that constructed a document without reading it from disk.</summary>
    public string ContentHash { get; init; } = string.Empty;
}
