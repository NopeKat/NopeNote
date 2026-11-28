using System;

namespace NopeNote.Windows.Services;

/// <summary>Shell state that has to survive a page rebuild, such as a language change.</summary>
public sealed class ShellSession
{
    /// <summary>The note that was open when the page was last torn down.</summary>
    public Guid? LastNoteId { get; set; }
}
