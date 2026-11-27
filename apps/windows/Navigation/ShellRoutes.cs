using NopeNote.Windows.Services;
using NopeNote.Windows.Views;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NopeNote.Windows.Navigation;

/// <summary>One shell destination: the nav item tag, the page type and the parameter
/// that page expects. The tag doubles as the stable automation token.</summary>
internal sealed record ShellRoute(string Tag, Type PageType, Func<AppServices, object> ParameterFactory);

/// <summary>Single table for the shell's pages; adding a page means adding one entry.</summary>
internal static class ShellRoutes
{
    internal static readonly ShellRoute Notes = new(
        "Notes",
        typeof(NotesPage),
        services => new NotesNavigation(null, services));

    internal static readonly ShellRoute Search = new(
        "Search",
        typeof(SearchPage),
        services => services);

    internal static readonly ShellRoute Trash = new(
        "Trash",
        typeof(TrashPage),
        services => services);

    internal static readonly ShellRoute Settings = new(
        "Settings",
        typeof(SettingsPage),
        services => services);

    internal static readonly IReadOnlyList<ShellRoute> All = new[] { Notes, Search, Trash, Settings };

    internal static ShellRoute? ForTag(string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return null;
        }

        return All.FirstOrDefault(route => string.Equals(route.Tag, tag, StringComparison.Ordinal));
    }

    /// <summary>Unregistered pages fall back to the notes shell.</summary>
    internal static ShellRoute ForPage(Type? pageType)
        => All.FirstOrDefault(route => route.PageType == pageType) ?? Notes;
}
