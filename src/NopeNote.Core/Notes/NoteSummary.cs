namespace NopeNote.Core.Notes;

/// <summary>A note as listed without its body.</summary>
public sealed record NoteSummary(
    Guid Id,
    string Title,
    DateTimeOffset Created,
    DateTimeOffset Updated,
    IReadOnlyList<string> Tags,
    bool Pinned,
    string RelativePath);
