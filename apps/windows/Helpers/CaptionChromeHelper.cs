using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NopeNote.Windows.Helpers;

/// <summary>
/// Removes the two pieces of native caption chrome the app does not want:
/// the classic window menu opened by a right click on the title bar, and the
/// shell tooltips that appear when hovering the minimize, maximize and close
/// buttons. Dragging, double-click maximize, the caption buttons themselves
/// and the Alt+Space menu keep working.
/// </summary>
internal static class CaptionChromeHelper
{
    private const uint WmNcDestroy = 0x0002;
    private const uint WmNcLButtonDown = 0x00A1;
    private const uint WmNcLButtonUp = 0x00A2;
    private const uint WmNcLButtonDoubleClick = 0x00A3;
    private const uint WmNcRButtonDown = 0x00A4;
    private const uint WmNcRButtonUp = 0x00A5;
    private const uint WmNcRButtonDoubleClick = 0x00A6;

    // The caption icon is its own hit-test target, not part of the caption.
    private const int HtSysMenu = 3;

    private const uint EventObjectCreate = 0x8000;
    private const uint EventObjectShow = 0x8002;
    private const uint WineventOutOfContext = 0x0000;

    private const int SwHide = 0;
    private const int GwlStyle = -16;
    private const int WsChild = unchecked((int)0x40000000);

    // The shell hosts the caption button tooltip in this window class.
    private const string CaptionTooltipClass = "MicrosoftWindowsTooltip";

    private static readonly nuint CaptionSubclassId = 1;

    // Native code stores the raw function pointers, so the delegates have to
    // stay alive for as long as the window or the hook exists.
    private static readonly SubclassProc SubclassProcReference = SuppressCaptionMenu;
    private static readonly nint SubclassProcPointer =
        Marshal.GetFunctionPointerForDelegate(SubclassProcReference);

    private static readonly WinEventProc WinEventProcReference = HideCaptionTooltip;
    private static readonly nint WinEventProcPointer =
        Marshal.GetFunctionPointerForDelegate(WinEventProcReference);

    private static nint _tooltipHook;
    private static int _captionWindows;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProc(
        nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nint refData);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void WinEventProc(
        nint hook, uint evt, nint hWnd, int idObject, int idChild, uint thread, uint time);

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
    private static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint module,
        nint proc,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int cmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassName(nint hWnd, StringBuilder name, int max);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint hWnd, int index);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentProcessId();

    /// <summary>
    /// Installs the caption subclass and starts watching for the shell's
    /// caption button tooltips. Call this after the title bar has been
    /// registered so this subclass sees the non-client messages first.
    /// </summary>
    internal static bool SuppressCaptionChrome(nint hWnd)
    {
        if (hWnd == 0)
        {
            return false;
        }

        bool applied = SetWindowSubclass(hWnd, SubclassProcPointer, CaptionSubclassId, 0);

        if (applied)
        {
            _captionWindows++;
        }

        if (_tooltipHook == 0)
        {
            _tooltipHook = SetWinEventHook(
                EventObjectCreate,
                EventObjectShow,
                module: 0,
                proc: WinEventProcPointer,
                processId: GetCurrentProcessId(),
                threadId: 0,
                flags: WineventOutOfContext);
        }

        return applied;
    }

    private static void HideCaptionTooltip(
        nint hook, uint evt, nint hWnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hWnd == 0 || idObject != 0)
        {
            return;
        }

        // Only the shell's top-level caption tooltip is of interest; a child
        // window with the same class belongs to some other control.
        if ((GetWindowLong(hWnd, GwlStyle) & WsChild) != 0)
        {
            return;
        }

        if (!IsClass(hWnd, CaptionTooltipClass))
        {
            return;
        }

        ShowWindow(hWnd, SwHide);
    }

    private static bool IsClass(nint hWnd, string name)
    {
        const int Capacity = 128;
        var buffer = new StringBuilder(Capacity);
        int length = GetClassName(hWnd, buffer, Capacity);

        return length > 0
            && length < Capacity
            && string.Equals(buffer.ToString(), name, StringComparison.Ordinal);
    }

    private static nint SuppressCaptionMenu(
        nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nint refData)
    {
        switch (msg)
        {
            case WmNcRButtonDown:
            case WmNcRButtonUp:
            case WmNcRButtonDoubleClick:
                return 0;

            // wParam carries the hit-test code for non-client button messages.
            case WmNcLButtonDown:
            case WmNcLButtonUp:
            case WmNcLButtonDoubleClick:
                if (wParam == HtSysMenu)
                {
                    return 0;
                }

                break;

            // The subclass must come off before the window goes away. The
            // tooltip hook is process-wide, so it waits for the last window.
            case WmNcDestroy:
                RemoveWindowSubclass(hWnd, SubclassProcPointer, id);

                if (_captionWindows > 0)
                {
                    _captionWindows--;
                }

                if (_captionWindows == 0 && _tooltipHook != 0)
                {
                    UnhookWinEvent(_tooltipHook);
                    _tooltipHook = 0;
                }

                break;
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }
}
