namespace NopeNote.Core.Vault;

public sealed record TrashNoteSummary(
    Guid Id,
    string Title,
    DateTimeOffset Updated,
    DateTimeOffset DeletedAt);
