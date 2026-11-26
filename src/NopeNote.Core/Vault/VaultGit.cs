using System.Diagnostics;

namespace NopeNote.Core.Vault;

/// <summary>
/// Optional local git versioning, as described by the spec: repository-level
/// config only, lowercase English commit messages, no remotes, no history rewrite.
/// </summary>
public static class VaultGit
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public static async Task<bool> IsAvailableAsync()
        => await RunAsync(Path.GetTempPath(), "version", CancellationToken.None).ConfigureAwait(false) is not null;

    public static async Task<bool> InitializeAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        if (await RunAsync(rootPath, "init -b main", cancellationToken).ConfigureAwait(false) is null)
        {
            return false;
        }

        foreach (string setting in new[]
        {
            "config user.name NopeNote",
            "config user.email note@local",
            "config core.autocrlf false",
            "config core.gpgsign false",
            "config commit.gpgsign false",
        })
        {
            await RunAsync(rootPath, setting, cancellationToken).ConfigureAwait(false);
        }

        await RunAsync(rootPath, "add -A", cancellationToken).ConfigureAwait(false);
        return await RunAsync(rootPath, "commit -m \"chore: initialize vault\"", cancellationToken).ConfigureAwait(false) is not null;
    }

    public static async Task<bool> CommitAsync(string rootPath, string message, CancellationToken cancellationToken = default)
    {
        await RunAsync(rootPath, "add -A", cancellationToken).ConfigureAwait(false);
        return await RunAsync(rootPath, $"commit -m \"{message}\"", cancellationToken).ConfigureAwait(false) is not null;
    }

    /// <summary>
    /// Checks that HEAD exists and that the object database passes `git fsck`.
    /// A missing git binary or a broken repository returns false.
    /// </summary>
    public static async Task<bool> VerifyAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        string? head = await RunAsync(rootPath, "rev-parse --verify HEAD", cancellationToken).ConfigureAwait(false);
        if (head is null)
        {
            return false;
        }

        return await RunAsync(rootPath, "fsck --no-progress", cancellationToken).ConfigureAwait(false) is not null;
    }

    /// <summary>
    /// Runs git in the given directory. Returns null when git is missing, the
    /// command times out, or it exits non-zero.
    /// </summary>
    private static async Task<string?> RunAsync(string workingDirectory, string arguments, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            if (!process.Start())
            {
                return null;
            }

            using var timeout = new CancellationTokenSource(Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            Task<string> stdout = process.StandardOutput.ReadToEndAsync(linked.Token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(linked.Token);

            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            string output = await stdout.ConfigureAwait(false);
            _ = stderr;

            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }
}
