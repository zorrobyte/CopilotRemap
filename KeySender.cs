using System.Runtime.InteropServices;

namespace CopilotRemap;

public static class KeySender
{
    // Tags every synthetic event we send so KeyboardHook can recognize and ignore it.
    public const long SyntheticInputMarker = 0x434B5253; // "CKRS"

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL, wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // Keys that require KEYEVENTF_EXTENDEDKEY to be interpreted correctly by SendInput.
    private static readonly HashSet<Keys> ExtendedKeys = new()
    {
        Keys.RControlKey, Keys.RMenu,
        Keys.Insert, Keys.Delete, Keys.Home, Keys.End,
        Keys.Prior, Keys.Next, // PageUp / PageDown
        Keys.Left, Keys.Right, Keys.Up, Keys.Down,
        Keys.NumLock, Keys.PrintScreen, Keys.Cancel, // Ctrl+Break
        Keys.Divide // numpad "/"
    };

    public static void Send(KeystrokeCombo combo)
    {
        var vks = ExpandToVks(combo).ToArray();
        var downs = vks.Select(vk => Build(vk, keyUp: false));
        var ups = vks.Reverse().Select(vk => Build(vk, keyUp: true));
        var all = downs.Concat(ups).ToArray();

        var sent = SendInput((uint)all.Length, all, Marshal.SizeOf<INPUT>());
        if (sent != all.Length)
            throw new InvalidOperationException(
                $"SendInput failed (sent {sent}/{all.Length}). Error: {Marshal.GetLastWin32Error()}");
    }

    private static IEnumerable<Keys> ExpandToVks(KeystrokeCombo combo)
    {
        if (combo.Modifiers.HasFlag(KeyModifiers.Control)) yield return Keys.ControlKey;
        if (combo.Modifiers.HasFlag(KeyModifiers.Shift)) yield return Keys.ShiftKey;
        if (combo.Modifiers.HasFlag(KeyModifiers.Alt)) yield return Keys.Menu;
        if (combo.Modifiers.HasFlag(KeyModifiers.Win)) yield return Keys.LWin;
        yield return combo.MainKey;
    }

    private static INPUT Build(Keys vk, bool keyUp)
    {
        uint flags = keyUp ? KEYEVENTF_KEYUP : 0;
        if (ExtendedKeys.Contains(vk)) flags |= KEYEVENTF_EXTENDEDKEY;

        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    wScan = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = (IntPtr)SyntheticInputMarker
                }
            }
        };
    }
}
