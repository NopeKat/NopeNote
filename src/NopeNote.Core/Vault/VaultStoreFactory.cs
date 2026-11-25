namespace NopeNote.Core.Vault;

public static class VaultStoreFactory
{
    private static readonly System.Text.UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Initializes a new vault in an empty directory and then opens it.
    /// The caller decides the location; the directory must not already hold files.
    /// </summary>
    public static Task<VaultOpenResult> CreateAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
        => Task.Run(() => CreateCore(rootPath, cancellationToken), cancellationToken);

    private static VaultOpenResult CreateCore(
        string rootPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string root = Path.GetFullPath(rootPath);
        if (File.Exists(Path.Combine(root, VaultLayout.MarkerFileName)))
        {
            throw new VaultFormatException($"a vault already exists at {root}.");
        }

        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new VaultFormatException($"directory is not empty: {root}.");
        }

        Directory.CreateDirectory(root);
        foreach (string directory in VaultLayout.RequiredDirectories)
        {
            Directory.CreateDirectory(VaultLayout.ToSystemPath(root, directory));
        }

        string marker = System.Text.Json.JsonSerializer.Serialize(
            new
            {
                schema = "nopenote.vault",
                formatVersion = 1,
                repoId = Guid.NewGuid().ToString("D"),
                createdAt = Notes.NoteFrontMatterWriter.FormatTimestamp(DateTimeOffset.UtcNow),
                encryption = (object?)null,
                features = new { },
            },
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        WriteText(Path.Combine(root, VaultLayout.MarkerFileName), marker.Replace("\r\n", "\n") + "\n");
        WriteText(
            Path.Combine(root, ".gitignore"),
            ".tmp/\n*.tmp-*\n.history/\n.DS_Store\nThumbs.db\ndesktop.ini\n");
        WriteText(
            Path.Combine(root, ".gitattributes"),
            "* text=auto eol=lf\n*.png binary\n*.jpg binary\n*.ico binary\n");

        VaultSettings.Write(root, VaultSettings.Default);

        return OpenCore(root, cancellationToken);
    }

    private static void WriteText(string path, string text)
        => File.WriteAllText(path, text, Utf8NoBom);

    public static Task<VaultOpenResult> OpenAsync(string rootPath, CancellationToken cancellationToken = default)
        => Task.Run(() => OpenCore(rootPath, cancellationToken), cancellationToken);

    private static VaultOpenResult OpenCore(string rootPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(rootPath))
        {
            return new VaultOpenResult(
                VaultOpenStatus.NotAVault,
                null,
                "仓库目录不存在。");
        }

        VaultMetadata metadata;
        try
        {
            metadata = VaultMetadataReader.Read(rootPath);
        }
        catch (VaultFormatException exception)
        {
            return new VaultOpenResult(
                VaultOpenStatus.NotAVault,
                null,
                exception.Message);
        }

        if (!string.Equals(metadata.Schema, "nopenote.vault", StringComparison.Ordinal))
        {
            return new VaultOpenResult(
                VaultOpenStatus.NotAVault,
                null,
                ".nopenote schema 不是 nopenote.vault。");
        }

        if (metadata.FormatVersion > 1)
        {
            var readOnlyStore = new LocalVaultStore(
                rootPath,
                metadata,
                new VaultCapabilities(false, false, false, false));
            return new VaultOpenResult(
                VaultOpenStatus.ReadOnly,
                readOnlyStore,
                $"仓库格式版本 {metadata.FormatVersion} 高于当前客户端支持的 1。");
        }

        if (metadata.Encrypted)
        {
            var readOnlyStore = new LocalVaultStore(
                rootPath,
                metadata,
                new VaultCapabilities(false, false, false, false));
            return new VaultOpenResult(
                VaultOpenStatus.ReadOnly,
                readOnlyStore,
                "仓库已加密，当前版本不支持解密写入。");
        }

        VaultCapabilities capabilities;
        try
        {
            capabilities = VaultProbe.Probe(rootPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var readOnlyStore = new LocalVaultStore(
                rootPath,
                metadata,
                new VaultCapabilities(false, false, false, false));
            return new VaultOpenResult(
                VaultOpenStatus.ReadOnly,
                readOnlyStore,
                $"目录不可写或能力探测失败：{exception.Message}");
        }

        var store = new LocalVaultStore(rootPath, metadata, capabilities);
        return new VaultOpenResult(VaultOpenStatus.Opened, store, "仓库已打开。");
    }
}
