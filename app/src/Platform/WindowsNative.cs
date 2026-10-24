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

    public static bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>Removes the window from the taskbar and Alt+Tab (the tray icon replaces it).</summary>
    public static void HideFromTaskbar(long hwnd)
    {
        if (!IsWindows || hwnd == 0)
            return;
        try
        {
            var handle = new IntPtr(hwnd);
            long style = GetWindowLongPtrW(handle, GwlExStyle).ToInt64();
            style = (style | WsExToolWindow) & ~WsExAppWindow;
            // The taskbar only notices the new style after the window is re-shown.
            ShowWindow(handle, SwHide);
            SetWindowLongPtrW(handle, GwlExStyle, new IntPtr(style));
            ShowWindow(handle, SwShowNoActivate);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            GD.PushWarning($"Could not hide the window from the taskbar: {e.Message}");
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
