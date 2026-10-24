using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Godot;

namespace DigitalDoll.Platform;

/// <summary>Win32 calls that Godot does not expose. Every method is a no-op elsewhere.</summary>
public static class WindowsNative
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExAppWindow = 0x00040000;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private const int SwShow = 5;

    public static bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>Removes the window from the taskbar and Alt+Tab (the tray icon replaces it).</summary>
    public static void HideFromTaskbar(long hwnd) => SetTaskbarStyle(hwnd, visible: false);

    /// <summary>Restores a normal taskbar button (used by the editor window).</summary>
    public static void ShowInTaskbar(long hwnd) => SetTaskbarStyle(hwnd, visible: true);

    private static void SetTaskbarStyle(long hwnd, bool visible)
    {
        if (!IsWindows || hwnd == 0)
            return;
        try
        {
            var handle = new IntPtr(hwnd);
            long style = GetWindowLongPtrW(handle, GwlExStyle).ToInt64();
            long updated = visible
                ? (style & ~WsExToolWindow) | WsExAppWindow
                : (style | WsExToolWindow) & ~WsExAppWindow;
            if (updated == style)
                return;
            // The taskbar only notices the new style after the window is re-shown.
            ShowWindow(handle, SwHide);
            SetWindowLongPtrW(handle, GwlExStyle, new IntPtr(updated));
            ShowWindow(handle, visible ? SwShow : SwShowNoActivate);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            GD.PushWarning($"Could not change the taskbar style: {e.Message}");
        }
    }

    private static readonly HashSet<string> ShellClasses = new()
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
    };

    /// <summary>
    /// Visible application windows in screen pixels, top of the Z-order first. Minimised,
    /// cloaked (other virtual desktops, suspended apps), tool and shell windows, and
    /// <paramref name="exclude"/> (our own window) are left out. Empty elsewhere.
    /// </summary>
    public static List<(long Id, Rect2I Bounds)> VisibleWindows(long exclude)
    {
        var result = new List<(long, Rect2I)>();
        if (!IsWindows)
            return result;
        try
        {
            var name = new StringBuilder(256);
            EnumWindowsProc callback = (hwnd, _) =>
            {
                if ((long)hwnd == exclude || !IsWindowVisible(hwnd) || IsIconic(hwnd))
                    return true;
                long style = GetWindowLongPtrW(hwnd, GwlExStyle).ToInt64();
                if ((style & WsExToolWindow) != 0 || GetWindowTextLengthW(hwnd) == 0)
                    return true;
                if (DwmGetWindowAttribute(hwnd, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return true;
                name.Clear();
                GetClassNameW(hwnd, name, name.Capacity);
                if (ShellClasses.Contains(name.ToString()))
                    return true;
                if (DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out NativeRect r, Marshal.SizeOf<NativeRect>()) != 0)
                    return true;
                if (r.Right - r.Left > 0 && r.Bottom - r.Top > 0)
                    result.Add(((long)hwnd, new Rect2I(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)));
                return true;
            };
            EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            GD.PushWarning($"Could not list windows: {e.Message}");
        }
        return result;
    }

    private const int DwmwaExtendedFrameBounds = 9;
    private const int DwmwaCloaked = 14;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out NativeRect value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
