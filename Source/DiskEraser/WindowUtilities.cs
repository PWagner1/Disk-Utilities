namespace DiskEraser;

public sealed class WindowUtilities
{
    private const int GWL_STYLE = -16;
    private const uint WS_MAXIMIZEBOX = 0x00010000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;

    /// <summary>
    /// Attempts to remove the maximize button from the window frame on Windows by clearing WS_MAXIMIZEBOX.
    /// This replaces the WinForms-style `MaximizeBox = false;` which does not exist on WinUI Window.
    /// This method is best-effort and silently fails on platforms where user32 calls are unavailable.
    /// </summary>
    public static void TryDisableMaximizeBox(Window? window)
    {
        if (window == null)
        {
            return;
        }

        try
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            if (IntPtr.Size == 8)
            {
                var stylePtr = GetWindowLongPtr(hwnd, GWL_STYLE);
                long style = stylePtr.ToInt64();
                style &= ~WS_MAXIMIZEBOX;
                SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style));
            }
            else
            {
                int style = GetWindowLong(hwnd, GWL_STYLE);
                style &= ~(int)WS_MAXIMIZEBOX;
                SetWindowLong(hwnd, GWL_STYLE, style);
            }

            // Force a frame update so the caption buttons refresh
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }
        catch
        {
            // Ignore failures - platform may not support these calls
        }
    }

    /// <summary>
    /// Retrieves information about the specified window based on the provided offset.
    /// </summary>
    /// <remarks>This function is typically used to retrieve window properties such as styles or user data.
    /// Ensure that the window handle is valid and that the offset specified by nIndex is appropriate for the intended
    /// use. On 32-bit versions of Windows, this function is equivalent to GetWindowLong.</remarks>
    /// <param name="hWnd">A handle to the window whose information is to be retrieved. This handle must refer to a valid window.</param>
    /// <param name="nIndex">The zero-based offset to the value to be retrieved. This parameter specifies which attribute or property of the
    /// window to access.</param>
    /// <returns>An IntPtr representing the value at the specified offset for the given window. Returns IntPtr.Zero if the
    /// function fails. To obtain extended error information, call GetLastError.</returns>
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}