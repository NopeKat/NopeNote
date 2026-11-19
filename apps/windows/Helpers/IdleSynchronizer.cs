using Microsoft.UI.Dispatching;

namespace NopeNote.Windows.Helpers;

public static class IdleSynchronizer
{
    private static DispatcherQueue? _queue;

    public static void Init()
    {
        _queue = DispatcherQueue.GetForCurrentThread();
    }

    public static string TryWait(out string logMessage)
    {
        logMessage = "idle synchronizer initialized";
        return _queue is null ? "IdleSynchronizer.Init() was not called." : string.Empty;
    }
}
