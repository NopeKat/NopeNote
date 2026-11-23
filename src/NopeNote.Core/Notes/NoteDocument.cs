namespace NopeNote.Core.Notes;

/// <summary>A note read in full, keeping the original front matter lines.</summary>
public sealed record NoteDocument(
    NoteSummary Summary,
    string Body,
    IReadOnlyList<string> FrontMatterLines,
    string RawText);
