using NopeNote.Windows.Helpers;
using System.Threading.Tasks;

namespace NopeNote.Windows.Services;

/// <summary>Runs the idle probe off the UI thread for the automation helper button.</summary>
internal static class AutomationIdleProbe
{
    internal static async Task<(string Error, string Log)> RunAsync()
    {
        string error = string.Empty;
        string log = string.Empty;

        // 探测放到后台，结果回到 UI 线程再写入辅助控件。
        await Task.Run(() =>
        {
            error = IdleSynchronizer.TryWait(out log);
        });

        return (error, log);
    }
}
