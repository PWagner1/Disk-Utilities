namespace DiskUtilities.Shared;

/// <summary>
/// Shared WinUI window chrome helpers (Mica, extended title bar, size).
/// </summary>
public static class WindowChrome
{
    /// <summary>Applies a Mica backdrop when supported; no-op on unsupported platforms.</summary>
    public static void TryApplyMica(Window window, MicaKind kind = MicaKind.BaseAlt)
    {
        try
        {
            if (MicaController.IsSupported())
            {
                window.SystemBackdrop = new MicaBackdrop { Kind = kind };
            }
        }
        catch
        {
            // Mica not supported (e.g. older Windows); keep default background
        }
    }

    /// <summary>Extends content into the title bar and optionally sets the drag region element.</summary>
    public static void TryExtendTitleBar(Window window, UIElement? titleBar = null)
    {
        try
        {
            window.ExtendsContentIntoTitleBar = true;
            if (titleBar != null)
            {
                window.SetTitleBar(titleBar);
            }
        }
        catch
        {
            // Fall back to default title bar
        }
    }

    /// <summary>Sets the window client size via AppWindow (WinUI Window has no Width/Height in XAML).</summary>
    public static void TrySetSize(Window window, int width, int height)
    {
        try
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(window);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        }
        catch
        {
            // Ignore if AppWindow is unavailable
        }
    }

    private const int GwlStyle = -16;
    private const uint WsMaximizeBox = 0x00010000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;

    /// <summary>
    /// Best-effort removal of the maximize button (clears WS_MAXIMIZEBOX).
    /// WinUI Window has no MaximizeBox property.
    /// </summary>
    public static void TryDisableMaximizeBox(Window? window)
    {
        if (window == null)
        {
            return;
        }

        try
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(window);
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            if (IntPtr.Size == 8)
            {
                IntPtr stylePtr = GetWindowLongPtr(hwnd, GwlStyle);
                long style = stylePtr.ToInt64();
                style &= ~WsMaximizeBox;
                SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(style));
            }
            else
            {
                int style = GetWindowLong(hwnd, GwlStyle);
                style &= ~(int)WsMaximizeBox;
                SetWindowLong(hwnd, GwlStyle, style);
            }

            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
        }
        catch
        {
            // Ignore failures — platform may not support these calls
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
