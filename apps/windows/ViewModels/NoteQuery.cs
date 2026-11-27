namespace NopeNote.Windows.ViewModels;

/// <summary>
/// 一次列表/搜索查询的全部条件。两个入口（笔记列表过滤与搜索页）都归到同一个求值器，
/// 区别只在 <see cref="UseSyntax"/> 与额外的时间、仅标题条件。
/// </summary>
public sealed record NoteQuery
{
    public string Text { get; init; } = string.Empty;

    public string? Tag { get; init; }

    public string? Folder { get; init; }

    /// <summary>只保留最近 N 天更新过的笔记；0 表示不限。</summary>
    public int Days { get; init; }

    public bool TitleOnly { get; init; }

    /// <summary>是否解析 tag: / folder: / title: 语法；false 时整串按子串匹配。</summary>
    public bool UseSyntax { get; init; }
}
