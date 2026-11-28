using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using NopeNote.Core.Vault;
using NopeNote.Windows.Models;
using NopeNote.Windows.Services;

namespace NopeNote.Windows.ViewModels;

public sealed class NotesViewModel : INotifyPropertyChanged
{
    private readonly List<Note> _allNotes = new();

    private IVaultStore? _store;
    private IReadOnlyList<Note> _notes = Array.Empty<Note>();

    public event PropertyChangedEventHandler? PropertyChanged;

    // 过滤结果整份替换、一次通知，避免清空后逐项添加时触发 N 次集合变更。
    public IReadOnlyList<Note> Notes
    {
        get => _notes;
        private set
        {
            _notes = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Notes)));
        }
    }

    public IVaultStore Store
        => _store ?? throw new InvalidOperationException("the vault store has not been attached yet.");

    public IVaultStore? StoreOrNull => _store;

    public bool IsEmpty => Notes.Count == 0;

    public bool IsLoaded { get; private set; }

    public bool IsLoading { get; private set; }

    public string CurrentQuery { get; private set; } = string.Empty;

    public string? TagFilter { get; private set; }

    public string? FolderFilter { get; private set; }

    public IReadOnlyList<Note> AllNotes => _allNotes;

    public void SetTagFilter(string? tag)
    {
        TagFilter = string.IsNullOrWhiteSpace(tag) ? null : tag;
        ApplyFilter(CurrentQuery);
    }

    public void SetFolderFilter(string? folder)
    {
        FolderFilter = string.IsNullOrWhiteSpace(folder) ? null : folder;
        ApplyFilter(CurrentQuery);
    }

    public IReadOnlyList<string> GetAvailableFolders()
    {
        var folders = new List<string>();
        foreach (Note note in _allNotes)
        {
            string path = note.Folder;
            while (path.Length > 0)
            {
                if (!folders.Exists(existing =>
                        string.Equals(existing, path, StringComparison.OrdinalIgnoreCase)))
                {
                    folders.Add(path);
                }

                int separator = path.LastIndexOf('/');
                path = separator > 0 ? path[..separator] : string.Empty;
            }
        }

        folders.Sort(StringComparer.OrdinalIgnoreCase);
        return folders;
    }

    public IReadOnlyList<string> GetAvailableTags()
    {
        var tags = new List<string>();
        foreach (Note note in _allNotes)
        {
            foreach (string tag in note.Tags)
            {
                if (!tags.Exists(existing =>
                        string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase)))
                {
                    tags.Add(tag);
                }
            }
        }

        tags.Sort(StringComparer.OrdinalIgnoreCase);
        return tags;
    }

    public void Attach(IVaultStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task LoadAsync(NoteSearchIndex index)
    {
        IsLoading = true;
        try
        {
            var loaded = new List<Note>();
            IReadOnlyList<Note> notes = await Note.LoadAllAsync(Store, index);
            foreach (Note note in notes)
            {
                loaded.Add(note);
            }

            // Replace the collection only after every note loaded; a failure
            // keeps the previous list instead of leaving an empty page.
            _allNotes.Clear();
            foreach (Note note in loaded)
            {
                _allNotes.Add(note);
            }

            IsLoaded = true;
            ApplyFilter(CurrentQuery);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void ApplyFilter(string query)
    {
        Apply(new NoteQuery
        {
            Text = query,
            Tag = TagFilter,
            Folder = FolderFilter,
        });
    }

    /// <summary>标签、文件夹、时间范围、仅标题和 tag:/folder:/title: 语法。</summary>
    public void ApplySearch(
        string query,
        string? tag,
        string? folder,
        int days,
        bool titleOnly)
    {
        Apply(new NoteQuery
        {
            Text = query,
            Tag = string.IsNullOrWhiteSpace(tag) ? null : tag,
            Folder = string.IsNullOrWhiteSpace(folder) ? null : folder,
            Days = days,
            TitleOnly = titleOnly,
            UseSyntax = true,
        });
    }

    /// <summary>Two-stage search: summary fields cut the candidates first, then
    /// only the rest have their bodies read before the final match.</summary>
    public async Task ApplySearchAsync(
        string query,
        string? tag,
        string? folder,
        int days,
        bool titleOnly,
        Action<int, int>? onProgress,
        CancellationToken cancellationToken)
    {
        var noteQuery = new NoteQuery
        {
            Text = query,
            Tag = string.IsNullOrWhiteSpace(tag) ? null : tag,
            Folder = string.IsNullOrWhiteSpace(folder) ? null : folder,
            Days = days,
            TitleOnly = titleOnly,
            UseSyntax = true,
        };

        var candidates = new List<Note>(_allNotes.Count);
        foreach (Note note in _allNotes)
        {
            if (MatchesSummary(note, noteQuery))
            {
                candidates.Add(note);
            }
        }

        if (NeedsBody(noteQuery) && candidates.Count > 0)
        {
            int done = 0;
            foreach (Note note in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await note.EnsureBodyAsync(Store);
                done++;
                if (done % ProgressStep == 0)
                {
                    onProgress?.Invoke(done, candidates.Count);
                }
            }

            onProgress?.Invoke(done, candidates.Count);
        }

        Apply(noteQuery);
    }

    private const int ProgressStep = 64;

    /// <summary>Only a non-empty keyword outside title-only mode needs the body.</summary>
    private static bool NeedsBody(NoteQuery query)
        => !string.IsNullOrWhiteSpace(query.Text) && !query.TitleOnly;

    /// <summary>Matches on fields the summary already carries. Tokens that could
    /// still hit the body are kept as candidates.</summary>
    private static bool MatchesSummary(Note note, NoteQuery query)
    {
        if (query.Tag is not null && !HasTag(note, query.Tag))
        {
            return false;
        }

        if (query.Folder is not null && !IsInFolder(note, query.Folder))
        {
            return false;
        }

        if (query.Days > 0 && note.Date < DateTime.Now.AddDays(-query.Days))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(query.Text) || !query.UseSyntax)
        {
            return true;
        }

        foreach (string token in query.Text.Split(
                     ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
            {
                if (!HasTag(note, token[4..]))
                {
                    return false;
                }

                continue;
            }

            if (token.StartsWith("folder:", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsInFolder(note, token[7..]))
                {
                    return false;
                }

                continue;
            }

            // Outside title-only mode any token may still hit the body or tags,
            // so only title-only searches can be rejected from the summary.
            if (!query.TitleOnly)
            {
                continue;
            }

            string term = token.StartsWith("title:", StringComparison.OrdinalIgnoreCase)
                ? token[6..]
                : token;
            if (term.Length == 0)
            {
                continue;
            }

            if (note.DisplayTitle.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>唯一的求值入口：两个页面都走这里，只是传不同的条件。</summary>
    public void Apply(NoteQuery query)
    {
        CurrentQuery = query.Text;

        var pinned = new List<Note>(_allNotes.Count);
        var others = new List<Note>(_allNotes.Count);

        foreach (Note note in _allNotes)
        {
            if (!Matches(note, query))
            {
                continue;
            }

            (note.IsPinned ? pinned : others).Add(note);
        }

        // 列表顺序本身是“最近更新在前”，这里只把置顶的一组提到最前面。
        pinned.AddRange(others);
        Notes = pinned;
    }

    private static bool Matches(Note note, NoteQuery query)
    {
        if (query.Tag is not null && !HasTag(note, query.Tag))
        {
            return false;
        }

        if (query.Folder is not null && !IsInFolder(note, query.Folder))
        {
            return false;
        }

        if (query.Days > 0 && note.Date < DateTime.Now.AddDays(-query.Days))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(query.Text))
        {
            return true;
        }

        return query.UseSyntax
            ? MatchesSyntax(note, query)
            : MatchesPlain(note, query.Text);
    }

    /// <summary>整串子串匹配：标题、正文或任一标签命中即可。</summary>
    private static bool MatchesPlain(Note note, string query)
    {
        if (note.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            note.DisplayTitle.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        foreach (string tag in note.Tags)
        {
            if (tag.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>逐 token 匹配，支持 tag: / folder: / title: 前缀，每个 token 都必须命中。</summary>
    private static bool MatchesSyntax(Note note, NoteQuery query)
    {
        foreach (string token in query.Text.Split(
                     ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
            {
                if (!HasTag(note, token[4..]))
                {
                    return false;
                }

                continue;
            }

            if (token.StartsWith("folder:", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsInFolder(note, token[7..]))
                {
                    return false;
                }

                continue;
            }

            bool titleToken = token.StartsWith("title:", StringComparison.OrdinalIgnoreCase);
            string term = titleToken ? token[6..] : token;
            if (term.Length == 0)
            {
                continue;
            }

            bool hit = note.DisplayTitle.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
            if (!hit && !query.TitleOnly)
            {
                hit = note.Text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
                foreach (string noteTag in note.Tags)
                {
                    if (noteTag.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hit = true;
                        break;
                    }
                }
            }

            if (!hit)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsInFolder(Note note, string folder)
    {
        string normalized = folder.Trim().Trim('/');
        if (normalized.Length == 0)
        {
            return true;
        }

        return string.Equals(note.Folder, normalized, StringComparison.OrdinalIgnoreCase) ||
               note.Folder.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasTag(Note note, string tag)
    {
        foreach (string existing in note.Tags)
        {
            if (string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // 只维护数据源，列表重建交给调用方统一做一次（RefreshNotes），
    // 避免新建/删除时在 ViewModel 和页面各过滤一遍。
    public void Add(Note note)
    {
        _allNotes.Insert(0, note);
    }

    public void Remove(Note note)
    {
        _allNotes.Remove(note);
    }
}
