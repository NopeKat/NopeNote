namespace NopeNote.Windows.Helpers;

/// <summary>
/// Core derives fallback titles as language-neutral sentinels; the UI owns the
/// localized text and must never show those English sentinels directly.
/// </summary>
internal static class NoteTitle
{
    private const string CoreUntitled = "(Untitled note)";
    private const string CoreBroken = "(Broken note)";
    private const string CoreUnreadable = "(Unreadable note)";

    internal static string Display(string? title)
    {
        if (string.IsNullOrWhiteSpace(title) || string.Equals(title, CoreUntitled, System.StringComparison.Ordinal))
        {
            return Strings.Get("NotesUntitled");
        }

        if (string.Equals(title, CoreBroken, System.StringComparison.Ordinal))
        {
            return Strings.Get("NotesBrokenNote");
        }

        if (string.Equals(title, CoreUnreadable, System.StringComparison.Ordinal))
        {
            return Strings.Get("NotesUnreadableNote");
        }

        return title;
    }
}
