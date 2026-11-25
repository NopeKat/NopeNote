using System;
using System.Runtime.InteropServices;

namespace NopeNote.Windows.Helpers;

/// <summary>
/// Registers one process-wide hotkey on a window and routes WM_HOTKEY to a
/// callback. The window subclass is chained in front of any existing one and
/// removes itself when the window is destroyed.
/// </summary>
internal static class GlobalHotkey
{
    private const uint WmHotkey = 0x0312;
    private const uint WmNcDestroy = 0x0002;

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;

    private const uint VirtualKeyN = 0x4E;
    private const int HotkeyId = 2;

    private static readonly nuint SubclassId = 2;

    private static readonly SubclassProc SubclassProcReference = HandleMessage;
    private static readonly nint SubclassProcPointer =
        Marshal.GetFunctionPointerForDelegate(SubclassProcReference);

    private static nint _window;
    private static Action? _callback;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProc(
        nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nint refData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        nint hWnd, nint proc, nuint id, nint refData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint hWnd, nint proc, nuint id);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    /// <summary>Registers Ctrl+Alt+N. Returns false when another app owns it.</summary>
    internal static bool RegisterQuickCapture(nint hWnd, Action callback)
    {
        if (hWnd == 0 || _window != 0)
        {
            return false;
        }

        if (!RegisterHotKey(hWnd, HotkeyId, ModControl | ModAlt | ModNoRepeat, VirtualKeyN))
        {
            return false;
        }

        if (!SetWindowSubclass(hWnd, SubclassProcPointer, SubclassId, 0))
        {
            UnregisterHotKey(hWnd, HotkeyId);
            return false;
        }

        _window = hWnd;
        _callback = callback;
        return true;
    }

    private static nint HandleMessage(
        nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nint refData)
    {
        if (msg == WmHotkey && wParam == HotkeyId && hWnd == _window)
        {
            _callback?.Invoke();
            return 0;
        }

        if (msg == WmNcDestroy && hWnd == _window)
        {
            UnregisterHotKey(hWnd, HotkeyId);
            RemoveWindowSubclass(hWnd, SubclassProcPointer, id);
            _window = 0;
            _callback = null;
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }
}
