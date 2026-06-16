using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CopilotRemap;

/// <summary>
/// Low-level keyboard hook that detects the Copilot key.
/// Handles two known key mappings:
///   - VK_LAUNCH_APP1 (0xB6) — some keyboards send this directly
///   - Win+Shift+F23 — other keyboards send this combo
/// Fires CopilotKeyDown on press and CopilotKeyUp on release.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

    private const int VK_LAUNCH_APP1 = 0xB6;
    private const int VK_F23 = 0x86;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_SHIFT = 0x10;
    private const int VK_SPACE = 0x20;

    public event Action? CopilotKeyDown;
    public event Action? CopilotKeyUp;
    public event Action? CopilotSpacePressed;

    private bool _copilotHeld;
    private bool _suppressNextSpaceUp;

    private IntPtr _hookId;
    private readonly LowLevelKeyboardProc _proc;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public KeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Install()
    {
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName), 0);

        if (_hookId == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to install keyboard hook. Error: {Marshal.GetLastWin32Error()}");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            if (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN)
            {
                if (IsCopilotKey(info))
                {
                    _copilotHeld = true;
                    CopilotKeyDown?.Invoke();
                    return (IntPtr)1; // Suppress the key
                }

                // Detect Space while Copilot key is held → QuickLaunch combo
                if (info.vkCode == VK_SPACE && _copilotHeld)
                {
                    _suppressNextSpaceUp = true;
                    CopilotSpacePressed?.Invoke();
                    return (IntPtr)1; // Suppress Space
                }
            }
            else if (wParam == WM_KEYUP || wParam == WM_SYSKEYUP)
            {
                if (IsCopilotKeyUp(info))
                {
                    _copilotHeld = false;
                    CopilotKeyUp?.Invoke();
                    return (IntPtr)1; // Suppress the key
                }

                // Suppress the Space release after a combo
                if (info.vkCode == VK_SPACE && _suppressNextSpaceUp)
                {
                    _suppressNextSpaceUp = false;
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool IsCopilotKey(KBDLLHOOKSTRUCT info)
    {
        // Direct Copilot key (VK_LAUNCH_APP1)
        if (info.vkCode == VK_LAUNCH_APP1)
            return true;

        // Win+Shift+F23 combo (some keyboards send this)
        if (info.vkCode == VK_F23)
        {
            bool winHeld = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0
                        || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
            bool shiftHeld = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            return winHeld && shiftHeld;
        }

        return false;
    }

    private static bool IsCopilotKeyUp(KBDLLHOOKSTRUCT info)
    {
        // On key-up we only check the vkCode — modifier state may have changed
        return info.vkCode == VK_LAUNCH_APP1 || info.vkCode == VK_F23;
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }
}
