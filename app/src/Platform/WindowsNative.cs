using System;
using System.Runtime.InteropServices;
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

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
