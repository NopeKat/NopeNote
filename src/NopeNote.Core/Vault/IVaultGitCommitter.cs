namespace NopeNote.Core.Vault;

public interface IVaultGitCommitter
{
    Task<bool> CommitAsync(string message, CancellationToken cancellationToken = default);
}

/// <summary>Commits through the git executable, using the repository-level config.</summary>
public sealed class ProcessGitCommitter : IVaultGitCommitter
{
    private readonly string _rootPath;

    public ProcessGitCommitter(string rootPath)
    {
        _rootPath = rootPath;
    }

    public Task<bool> CommitAsync(string message, CancellationToken cancellationToken = default)
        => VaultGit.CommitAsync(_rootPath, message, cancellationToken);
}
