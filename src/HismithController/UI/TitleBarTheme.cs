using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace HismithController;

// Tints the native Win32 title bar (caption) to match the app theme without taking over
// the non-client area, so Windows keeps drawing the minimize/maximize/close buttons.
// Relies on the Windows 11 DWM caption-color attributes (build 22000+); on Windows 10 the
// calls return a non-zero HRESULT and are silently ignored, leaving the default title bar.
internal static class TitleBarTheme
{
    // DwmSetWindowAttribute attribute IDs (dwmapi.h).
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20; // BOOL: dark-mode caption text/buttons
    private const int DWMWA_CAPTION_COLOR = 35;           // COLORREF: title bar fill
    private const int DWMWA_TEXT_COLOR = 36;              // COLORREF: title bar caption text

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int value, int size);

    // Reads the active theme's TitlebarColor/TextColor resources and pushes them to the window's
    // caption. Safe to call repeatedly (e.g. after a live light/dark switch). No-op if the window
    // has no handle yet — call from SourceInitialized or later.
    public static void Apply(Window window, bool isDark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        int darkMode = isDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

        // Deviation from the design: caption uses the window BackgroundColor (not TitlebarColor)
        // so the title bar blends seamlessly into the app body.
        if (TryGetThemeColor("BackgroundColor", out var caption))
        {
            int captionRef = ToColorRef(caption);
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionRef, sizeof(int));
        }

        if (TryGetThemeColor("TextColor", out var text))
        {
            int textRef = ToColorRef(text);
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textRef, sizeof(int));
        }
    }

    private static bool TryGetThemeColor(string key, out Color color)
    {
        if (Application.Current?.TryFindResource(key) is Color found)
        {
            color = found;
            return true;
        }
        color = default;
        return false;
    }

    // DWM COLORREF is 0x00BBGGRR (alpha unused), the reverse byte order of a WPF Color.
    private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);
}
