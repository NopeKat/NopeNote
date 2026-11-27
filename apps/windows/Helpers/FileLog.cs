using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace NopeNote.Windows.Helpers;

/// <summary>
/// One rolling log file under <c>%LOCALAPPDATA%\NopeNote\logs</c>, capped at 1 MB with
/// two rotated files. Logging must never take the app down, so every write failure is
/// swallowed and left to the debugger.
/// </summary>
internal static class FileLog
{
    private const long MaxBytes = 1_000_000;
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly object Gate = new();

    private static string? _path;

    internal static void Initialize()
    {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NopeNote",
            "logs");

        _path = Path.Combine(folder, "nopenote.log");
    }

    internal static string? CurrentPath => _path;

    /// <summary>Folder holding the log, created on demand for the settings button.</summary>
    internal static string? EnsureFolder()
    {
        string? path = _path;
        if (path is null)
        {
            return null;
        }

        string folder = Path.GetDirectoryName(path)!;
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return folder;
    }

    internal static void Info(string message) => Write("INFO", message, exception: null);

    internal static void Warn(string message) => Write("WARN", message, exception: null);

    internal static void Error(string message, Exception? exception = null)
        => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        string? path = _path;
        if (path is null)
        {
            return;
        }

        string stamp = DateTimeOffset.UtcNow.ToString(
            "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture);

        var line = new StringBuilder(160)
            .Append(stamp)
            .Append(' ')
            .Append(level)
            .Append(' ')
            .Append(message);

        if (exception is not null)
        {
            line.Append(" | ")
                .Append(exception.GetType().Name)
                .Append(": ")
                .Append(exception.Message);
        }

        line.Append('\n');

        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                RotateIfTooLarge(path);
                File.AppendAllText(path, line.ToString(), Utf8NoBom);
            }
            catch (Exception writeFailure) when (writeFailure is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"nopenote: log write failed: {writeFailure.Message}");
            }
        }
    }

    private static void RotateIfTooLarge(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length < MaxBytes)
        {
            return;
        }

        string second = path + ".2";
        if (File.Exists(second))
        {
            File.Delete(second);
        }

        string first = path + ".1";
        if (File.Exists(first))
        {
            File.Move(first, second);
        }

        File.Move(path, first);
    }
}
