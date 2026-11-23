namespace NopeNote.Core.Vault;

public static class VaultStoreFactory
{
    public static Task<VaultOpenResult> OpenAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(rootPath))
        {
            return Task.FromResult(new VaultOpenResult(
                VaultOpenStatus.NotAVault,
                null,
                "仓库目录不存在。"));
        }

        VaultMetadata metadata;
        try
        {
            metadata = VaultMetadataReader.Read(rootPath);
        }
        catch (VaultFormatException exception)
        {
            return Task.FromResult(new VaultOpenResult(
                VaultOpenStatus.NotAVault,
                null,
                exception.Message));
        }

        if (!string.Equals(metadata.Schema, "nopenote.vault", StringComparison.Ordinal))
        {
            return Task.FromResult(new VaultOpenResult(
                VaultOpenStatus.NotAVault,
                null,
                ".nopenote schema 不是 nopenote.vault。"));
        }

        if (metadata.FormatVersion > 1)
        {
            var readOnlyStore = new LocalVaultStore(
                rootPath,
                metadata,
                new VaultCapabilities(false, false, false, false));
            return Task.FromResult(new VaultOpenResult(
                VaultOpenStatus.ReadOnly,
                readOnlyStore,
                $"仓库格式版本 {metadata.FormatVersion} 高于当前客户端支持的 1。"));
        }

        if (metadata.Encrypted)
        {
            var readOnlyStore = new LocalVaultStore(
                rootPath,
                metadata,
                new VaultCapabilities(false, false, false, false));
            return Task.FromResult(new VaultOpenResult(
                VaultOpenStatus.ReadOnly,
                readOnlyStore,
                "仓库已加密，当前版本不支持解密写入。"));
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
            return Task.FromResult(new VaultOpenResult(
                VaultOpenStatus.ReadOnly,
                readOnlyStore,
                $"目录不可写或能力探测失败：{exception.Message}"));
        }

        var store = new LocalVaultStore(rootPath, metadata, capabilities);
        return Task.FromResult(new VaultOpenResult(VaultOpenStatus.Opened, store, "仓库已打开。"));
    }
}
