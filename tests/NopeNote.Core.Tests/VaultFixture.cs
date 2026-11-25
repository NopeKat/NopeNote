using NopeNote.Core.Vault;

namespace NopeNote.Core.Tests;

/// <summary>
/// Builds a minimal vault that satisfies the spec, so each test can corrupt
/// exactly one rule and assert the resulting finding.
/// </summary>
internal sealed class VaultFixture : IDisposable
{
    public static readonly Guid NoteId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    public static readonly Guid SecondNoteId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    // 与生产代码共用同一份目录清单，避免规范增减目录时测试数据跟着漂移。
    private static readonly IReadOnlyList<string> Directories = VaultLayout.RequiredDirectories;

    public VaultFixture(string? root = null)
    {
        Root = root ?? Path.Combine(Path.GetTempPath(), "nopenote-fixture-" + Guid.NewGuid().ToString("N"));
        Create();
    }

    public string Root { get; }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Root, recursive: true);
        }
    }

    public void Write(string relativePath, string content)
    {
        string path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var writer = new StreamWriter(path, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    public void EnsureDirectory(string relativePath)
    {
        Directory.CreateDirectory(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    public void Delete(string relativePath)
    {
        string path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public string NoteText(
        Guid id,
        string title = "Sample note",
        string updated = "2026-11-21T09:10:00Z",
        string body = "Body text.")
        => $"""
        ---
        id: {id:D}
        created: 2026-11-21T09:00:00Z
        updated: {updated}
        title: {title}
        tags: [工作, 周会]
        pinned: false
        ---

        {body}
        """;

    private void Create()
    {
        Directory.CreateDirectory(Root);
        foreach (string directory in Directories)
        {
            Directory.CreateDirectory(Path.Combine(Root, directory.Replace('/', Path.DirectorySeparatorChar)));
        }

        Write(".nopenote", """
        {
          "schema": "nopenote.vault",
          "formatVersion": 1,
          "repoId": "33333333-3333-4333-8333-333333333333",
          "createdAt": "2026-11-21T08:00:00Z",
          "encryption": null,
          "features": {}
        }
        """);
        Write(".gitignore", """
        .tmp/
        *.tmp-*
        .history/
        .DS_Store
        Thumbs.db
        """);
        Write($".vault/notes/{NoteId:D}.md", NoteText(NoteId));
    }
}
