using System.Runtime.InteropServices;

namespace MdViewer;

internal static class NativeMethods
{
    public const int WM_USER = 0x0400;
    public const int WM_KEYUP = 0x0101;
    public const int WM_VSCROLL = 0x0115;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int EM_SETTEXTMODE = WM_USER + 89;
    public const int TM_PLAINTEXT = 1;
    public const int TM_MULTILEVELUNDO = 8;
    public const int TM_MULTICODEPAGE = 32;

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void SetDarkTitleBar(IntPtr hwnd, bool dark)
    {
        int value = dark ? 1 : 0;
        try
        {
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref value, sizeof(int));
        }
        catch
        {
            // Older Windows: no dark title bar.
        }
    }
}
