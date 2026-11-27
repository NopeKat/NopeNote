using System.Security.Cryptography;
using System.Text;
using NopeNote.Core.Notes;

namespace NopeNote.Core.Vault;

/// <summary>Owns the vault's IO gate, directories and file primitives shared by the
/// note, trash and history stores. Every vault operation funnels through this single
/// gate so reads and writes never interleave on the same files.</summary>
internal sealed class VaultStorage
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    internal static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _ioGate = new(1, 1);

    internal VaultStorage(string rootPath, VaultCapabilities capabilities)
    {
        RootPath = rootPath;
        Capabilities = capabilities;
        NotesDirectory = Path.Combine(rootPath, ".vault", "notes");
        TrashDirectory = Path.Combine(rootPath, ".trash");
        HistoryDirectory = Path.Combine(rootPath, ".history");
        ConflictsDirectory = Path.Combine(rootPath, ".sync", "conflicts");
    }

    internal string RootPath { get; }

    internal VaultCapabilities Capabilities { get; }

    internal string NotesDirectory { get; }

    internal string TrashDirectory { get; }

    internal string HistoryDirectory { get; }

    internal string ConflictsDirectory { get; }

    internal string NotePath(Guid id) => Path.Combine(NotesDirectory, id.ToString("D") + ".md");

    internal string TrashPath(Guid id) => Path.Combine(TrashDirectory, id.ToString("D") + ".md");

    internal string HistoryPath(Guid id) => Path.Combine(HistoryDirectory, id.ToString("D"));

    internal static string NoteRelativePath(Guid id) => $".vault/notes/{id:D}.md";

    internal static string TrashRelativePath(Guid id) => $".trash/{id:D}.md";

    internal string Relative(string path) => Path.GetRelativePath(RootPath, path).Replace('\\', '/');

    internal async Task<T> RunAsync<T>(
        Func<CancellationToken, T> action,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!await _ioGate.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
        {
            throw new VaultTimeoutException(timeout);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        Task<T> task = Task.Run(
            () => InvokeWithRetry(() => action(timeoutSource.Token)),
            CancellationToken.None);

        try
        {
            return await task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            timeoutSource.Cancel();
            throw new VaultTimeoutException(timeout, mayHaveCompleted: true);
        }
        finally
        {
            _ = task.ContinueWith(
                static (finished, state) =>
                {
                    _ = finished.Exception;
                    ((SemaphoreSlim)state!).Release();
                },
                _ioGate,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    internal async Task RunAsync(
        Action<CancellationToken> action,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!await _ioGate.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
        {
            throw new VaultTimeoutException(timeout);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        Task task = Task.Run(
            () => InvokeWithRetry(() => action(timeoutSource.Token)),
            CancellationToken.None);

        try
        {
            await task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            timeoutSource.Cancel();
            throw new VaultTimeoutException(timeout, mayHaveCompleted: true);
        }
        finally
        {
            _ = task.ContinueWith(
                static (finished, state) =>
                {
                    _ = finished.Exception;
                    ((SemaphoreSlim)state!).Release();
                },
                _ioGate,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    internal void EnsureWritable()
    {
        if (!Capabilities.Writable)
        {
            throw new VaultReadOnlyException("vault was opened read-only; writes are disabled.");
        }
    }

    /// <summary>Spec 2.2/11.4: clear leftover temp files when a writable vault opens.
    /// Required subdirectories are kept; only their contents are removed.</summary>
    internal void ClearTempDirectory()
    {
        if (!Capabilities.Writable)
        {
            return;
        }

        string tempRoot = Path.Combine(RootPath, ".tmp");
        if (!Directory.Exists(tempRoot))
        {
            return;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(tempRoot, "*", SearchOption.AllDirectories);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (string file in files)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A file still held by another process is left for the next startup.
            }
        }
    }

    internal void WriteText(
        string path,
        string text,
        string? fallbackText = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (Capabilities.AtomicRename)
        {
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                WriteAllTextFlushed(temporary, text, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }

            return;
        }

        WriteAllTextFlushed(path, text, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        string readBack = File.ReadAllText(path);
        if (string.Equals(readBack, text, StringComparison.Ordinal))
        {
            return;
        }

        if (fallbackText is not null)
        {
            WriteAllTextFlushed(path, fallbackText, cancellationToken);
        }

        throw new IOException($"write verification failed for {Relative(path)}.");
    }

    /// <summary>Writes UTF-8 without BOM and flushes the file to disk before returning.</summary>
    private static void WriteAllTextFlushed(string path, string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using (var writer = new StreamWriter(stream, Utf8NoBom))
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.Write(text);
            writer.Flush();
            cancellationToken.ThrowIfCancellationRequested();
            stream.Flush(flushToDisk: true);
        }
    }

    /// <summary>
    /// Moves a vault file between the notes and trash directories with one rename when
    /// both live on the same volume. Cross-volume moves copy, verify by hash, then delete
    /// the source, so a failing move never leaves the same note in both places.
    /// </summary>
    internal static void MoveVaultFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        try
        {
            File.Move(source, destination, overwrite: true);
            return;
        }
        catch (IOException moveFailure)
        {
            string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                File.Copy(source, temporary, overwrite: true);
                if (!HasSameContent(source, temporary))
                {
                    throw new IOException(
                        $"copy verification failed for {Path.GetFileName(destination)}.",
                        moveFailure);
                }

                File.Move(temporary, destination, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }

            try
            {
                File.Delete(source);
            }
            catch (IOException)
            {
                File.Delete(destination);
                throw;
            }
        }
    }

    private static bool HasSameContent(string left, string right)
    {
        using FileStream leftStream = File.OpenRead(left);
        using FileStream rightStream = File.OpenRead(right);
        byte[] leftHash = SHA256.HashData(leftStream);
        byte[] rightHash = SHA256.HashData(rightStream);
        return leftHash.AsSpan().SequenceEqual(rightHash);
    }

    internal static NoteDocument ReadDocument(Guid id, string relativePath, string text)
        => FrontMatterParser.Parse(id, relativePath, text) with
        {
            ContentHash = ContentHash(text),
        };

    internal static string ContentHash(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    internal static bool TryParseCanonicalId(string name, out Guid id)
    {
        id = Guid.Empty;
        if (!Guid.TryParseExact(name, "D", out Guid parsed))
        {
            return false;
        }

        string canonical = parsed.ToString("D");
        if (!string.Equals(name, canonical, StringComparison.Ordinal))
        {
            return false;
        }

        id = parsed;
        return true;
    }

    internal static DateTimeOffset NextUpdated(DateTimeOffset previous)
    {
        DateTimeOffset candidate = UtcNowSeconds();
        return candidate > previous ? candidate : previous.AddSeconds(1);
    }

    internal static DateTimeOffset UtcNowSeconds()
    {
        long ticks = DateTime.UtcNow.Ticks;
        ticks -= ticks % TimeSpan.TicksPerSecond;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    /// <summary>Retries a vanished file up to three times with 1, 2 and 4 second waits.</summary>
    private static T InvokeWithRetry<T>(Func<T> action)
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                return action();
            }
            catch (Exception exception) when (attempt < 3 && IsTransient(exception))
            {
                attempt++;
                Thread.Sleep(TimeSpan.FromSeconds(1 << (attempt - 1)));
            }
        }
    }

    private static void InvokeWithRetry(Action action)
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                action();
                return;
            }
            catch (Exception exception) when (attempt < 3 && IsTransient(exception))
            {
                attempt++;
                Thread.Sleep(TimeSpan.FromSeconds(1 << (attempt - 1)));
            }
        }
    }

    private static bool IsTransient(Exception exception)
    {
        if (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }

        return exception is IOException io && (io.HResult & 0xFFFF) is 32 or 33;
    }
}
