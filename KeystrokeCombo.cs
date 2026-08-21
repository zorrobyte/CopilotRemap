namespace CopilotRemap;

// Named KeyModifiers (not "ModifierKeys") to avoid colliding with the
// inherited System.Windows.Forms.Control.ModifierKeys static property,
// which shadows an unqualified "ModifierKeys" inside any Control-derived class.
[Flags]
public enum KeyModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Win = 8
}

public readonly record struct KeystrokeCombo(KeyModifiers Modifiers, Keys MainKey)
{
    // Canonical order for both storage and display.
    private static readonly (KeyModifiers Flag, string Token)[] ModifierOrder =
    {
        (KeyModifiers.Control, "Ctrl"),
        (KeyModifiers.Shift, "Shift"),
        (KeyModifiers.Alt, "Alt"),
        (KeyModifiers.Win, "Win"),
    };

    private static readonly Dictionary<Keys, string> PrettyOverrides = new()
    {
        [Keys.Return] = "Enter",
        [Keys.Back] = "Backspace",
        [Keys.Escape] = "Esc",
        [Keys.Prior] = "PageUp",
        [Keys.Next] = "PageDown",
        [Keys.Capital] = "CapsLock",
        [Keys.Oemcomma] = ",",
        [Keys.OemPeriod] = ".",
        [Keys.OemQuestion] = "/",
        [Keys.Oemplus] = "=",
        [Keys.OemMinus] = "-",
        [Keys.OemOpenBrackets] = "[",
        [Keys.OemCloseBrackets] = "]",
        [Keys.OemPipe] = "\\",
        [Keys.OemQuotes] = "'",
        [Keys.OemSemicolon] = ";",
        [Keys.Oemtilde] = "`",
    };

    /// Round-trip-safe storage string, e.g. "Ctrl+Shift+C", "Alt+F4", "F5".
    public string Serialize()
    {
        var mods = Modifiers; // struct instance methods can't capture 'this' in a lambda
        var parts = ModifierOrder.Where(m => mods.HasFlag(m.Flag)).Select(m => m.Token)
            .Append(MainKey.ToString());
        return string.Join('+', parts);
    }

    public string ToDisplayString()
    {
        var mods = Modifiers;
        return string.Join('+', ModifierOrder.Where(m => mods.HasFlag(m.Flag)).Select(m => m.Token)
            .Append(PrettyMainKey(MainKey)));
    }

    public static string FormatModifiers(KeyModifiers mods) =>
        string.Join('+', ModifierOrder.Where(m => mods.HasFlag(m.Flag)).Select(m => m.Token));

    public static bool TryParse(string? value, out KeystrokeCombo combo)
    {
        combo = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var tokens = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return false;

        var mainToken = tokens[^1];
        var mods = KeyModifiers.None;

        for (int i = 0; i < tokens.Length - 1; i++)
        {
            var match = ModifierOrder.FirstOrDefault(m =>
                string.Equals(m.Token, tokens[i], StringComparison.OrdinalIgnoreCase));
            if (match.Token == null) return false; // unknown modifier token
            mods |= match.Flag;
        }

        if (!Enum.TryParse<Keys>(mainToken, ignoreCase: true, out var mainKey)) return false;
        if (IsModifierKey(mainKey)) return false; // reject modifier-only "combos"

        combo = new KeystrokeCombo(mods, mainKey);
        return true;
    }

    public static bool IsModifierKey(Keys k) => k is
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
        Keys.Menu or Keys.LMenu or Keys.RMenu or
        Keys.LWin or Keys.RWin or Keys.None;

    private static string PrettyMainKey(Keys k) => PrettyOverrides.TryGetValue(k, out var s) ? s : k.ToString();
}
