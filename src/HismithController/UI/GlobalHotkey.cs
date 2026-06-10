using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace HismithController;

// System-wide Alt+Space hotkey via Win32 RegisterHotKey. Unlike a WH_KEYBOARD_LL hook, this
// never observes other keystrokes — Windows posts a single WM_HOTKEY message only when the
// exact combo is pressed — so it does not match the keylogger heuristic AV/EDR engines flag
// (OpenPoints.md §4). The registration is global: WM_HOTKEY is delivered regardless of which
// window (or which control inside this app) has focus, so it replaces both the focused and
// unfocused emergency-stop paths the old hook needed. Trade-off: while registered, Alt+Space
// is swallowed system-wide, so it no longer opens the active window's system menu.
internal sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_NOREPEAT = 0x4000; // one event per press; ignore auto-repeat while held
    private const uint VK_SPACE = 0x20;

    // Arbitrary per-window identifier echoed back in WM_HOTKEY's wParam; only needs to be
    // unique among hotkeys this window registers (we register exactly one).
    private const int HotkeyId = 0xB10C;

    private HwndSource? _source;
    private bool _registered;

    // Raised on the window's UI thread (WM_HOTKEY is dispatched through the window's message
    // loop), so handlers may touch UI/VM state directly.
    public event Action? Pressed;

    // Must be called once the window's HWND exists (Window.SourceInitialized). The hotkey is
    // bound to this window's message queue.
    public void Register(IntPtr windowHandle)
    {
        if (_source is not null)
            return;

        _source = HwndSource.FromHwnd(windowHandle);
        _source!.AddHook(WndProc);
        _registered = RegisterHotKey(windowHandle, HotkeyId, MOD_ALT | MOD_NOREPEAT, VK_SPACE);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source is null)
            return;

        if (_registered)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }

        _source.RemoveHook(WndProc);
        _source = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
