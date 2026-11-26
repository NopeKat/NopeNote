using System.Text;

namespace NopeNote.Windows.Helpers;

/// <summary>Reports when a note body crosses the 2 MiB size hint from spec 3.</summary>
public static class BodySize
{
    public const int NoticeThreshold = 2 * 1024 * 1024;

    // No single char costs more than four UTF-8 bytes, so shorter text cannot cross the limit.
    private const int EarlyExitLength = NoticeThreshold / 4;

    public static bool IsOverNoticeThreshold(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length < EarlyExitLength)
        {
            return false;
        }

        return Encoding.UTF8.GetByteCount(text) > NoticeThreshold;
    }
}
