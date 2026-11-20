using System;

namespace NopeNote.Windows.Helpers;

public static class NoteSearchState
{
    private static string _query = string.Empty;

    public static string Query => _query;

    public static event EventHandler? QueryChanged;

    public static void SetQuery(string query)
    {
        if (_query == query)
        {
            return;
        }

        _query = query;
        QueryChanged?.Invoke(null, EventArgs.Empty);
    }
}
