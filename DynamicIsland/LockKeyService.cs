using System.Runtime.InteropServices;
using System.Windows;

namespace DynamicIsland;

/// <summary>
/// Raises <see cref="Toggled"/> (key name, now on) on the UI thread when Caps Lock or Num Lock is pressed.
/// Uses a low-level keyboard hook because the island never has keyboard focus.
/// </summary>
public sealed class LockKeyService : IDisposable
{
    readonly LowLevelKeyboardProc _proc;   // kept alive so the hook delegate isn't collected
    IntPtr _hook;
    bool _caps, _num, _capsDown, _numDown;

    public event Action<string, bool>? Toggled;

    public LockKeyService() => _proc = HookProc;

    public void Start()
    {
        _caps = (GetKeyState(VK_CAPITAL) & 1) != 0;
        _num = (GetKeyState(VK_NUMLOCK) & 1) != 0;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    }

    IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var vk = Marshal.ReadInt32(lParam);
            var message = (int)wParam;
            var down = message is WM_KEYDOWN or WM_SYSKEYDOWN;
            var up = message is WM_KEYUP or WM_SYSKEYUP;

            if (vk == VK_CAPITAL)
                Track(ref _capsDown, ref _caps, down, up, "Caps Lock");
            else if (vk == VK_NUMLOCK)
                Track(ref _numDown, ref _num, down, up, "Num Lock");
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    // The toggle state is tracked here: GetKeyState isn't reliable for a window without focus.
    void Track(ref bool isDown, ref bool state, bool down, bool up, string name)
    {
        if (down && !isDown)
        {
            isDown = true;
            state = !state;
            var on = state;
            Application.Current.Dispatcher.BeginInvoke(() => Toggled?.Invoke(name, on));
        }
        else if (up)
        {
            isDown = false;
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
            UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    const int VK_CAPITAL = 0x14, VK_NUMLOCK = 0x90;

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc proc, IntPtr hMod, uint threadId);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern short GetKeyState(int key);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? moduleName);
}
