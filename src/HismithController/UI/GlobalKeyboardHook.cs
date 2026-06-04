using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HismithController;

// System-wide low-level keyboard hook (WH_KEYBOARD_LL). Raises KeyDown for every key-down
// regardless of which window has focus, and does NOT swallow the keystroke — it still reaches
// the foreground app. Used so the Spacebar emergency stop works while HismithController is in
// the background.
internal sealed class GlobalKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    // Field-held so the GC cannot collect the delegate while the OS holds a native pointer to
    // it; a local would be reclaimed and the next keystroke would crash the hook callback.
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookHandle = IntPtr.Zero;

    // Raised on the thread that called Install() (the UI thread), because WH_KEYBOARD_LL
    // callbacks are dispatched to the installing thread's message loop. Argument is the Win32
    // virtual-key code. Consumers may therefore touch UI/VM state directly — no marshaling.
    public event Action<int>? KeyDown;

    public GlobalKeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Install()
    {
        if (_hookHandle != IntPtr.Zero)
            return;

        // WH_KEYBOARD_LL is global (dwThreadId 0) and is not injected into other processes, so
        // the module handle of the current process is sufficient for the hMod argument.
        using var module = Process.GetCurrentProcess().MainModule!;
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Win32 contract: when nCode < 0 the hook must pass the event on without inspecting it.
        // The callback must also return fast (within LowLevelHooksTimeout, ~300 ms) or Windows
        // silently drops the event; raising KeyDown into a fire-and-forget command satisfies this.
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            int vkCode = Marshal.ReadInt32(lParam);
            KeyDown?.Invoke(vkCode);
        }

        // Always chain so the keystroke still reaches the foreground app (non-swallowing).
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookHandle == IntPtr.Zero)
            return;

        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
