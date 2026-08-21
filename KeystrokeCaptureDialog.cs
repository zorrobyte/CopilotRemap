namespace CopilotRemap;

/// <summary>
/// Press-to-record dialog for capturing a keystroke combo. Listens to real
/// KeyDown/KeyUp events instead of a text box, so there is no risk of typos
/// and any combination the keyboard can physically produce can be captured.
/// </summary>
public sealed class KeystrokeCaptureDialog : Form
{
    public KeystrokeCombo? Combo { get; private set; }

    private readonly Label _previewLabel;
    private readonly Button _saveButton;

    private KeyModifiers _heldModifiers = KeyModifiers.None;
    private Keys? _pendingMainKey; // currently-down, not-yet-released main key
    private KeystrokeCombo? _committed; // last fully released (down+up) chord

    public KeystrokeCaptureDialog()
    {
        Text = "Record Keystroke";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        KeyPreview = true;
        ClientSize = new Size(360, 160);

        var instructions = new Label
        {
            Text = "Press the key combination you want to send.",
            Location = new Point(12, 12),
            AutoSize = true
        };

        _previewLabel = new Label
        {
            Text = "Press a key combination...",
            Location = new Point(12, 44),
            Size = new Size(336, 40),
            Font = new Font(SystemFonts.MenuFont!.FontFamily, 14, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };

        _saveButton = new Button
        {
            Text = "Save",
            Enabled = false,
            TabStop = false, // keep keyboard focus on the form itself, not this button
            Location = new Point(190, 100),
            Width = 75
        };
        _saveButton.Click += (_, _) =>
        {
            if (_committed == null) return;
            Combo = _committed;
            DialogResult = DialogResult.OK;
            Close();
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            TabStop = false, // keep keyboard focus on the form itself, not this button
            Location = new Point(273, 100),
            Width = 75
        };
        cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        // Deliberately not setting AcceptButton/CancelButton — Enter/Esc must
        // remain capturable as part of a combo. Save/Cancel are mouse-only.
        Controls.AddRange([instructions, _previewLabel, _saveButton, cancelButton]);
    }

    // By default WinForms treats Tab/Enter/Esc/arrows/Alt-combos as "dialog keys" and
    // routes them to ProcessDialogKey/ProcessCmdKey instead of OnKeyDown, so they'd never
    // reach our capture logic. Forcing every key to be treated as normal input keeps them
    // all flowing through OnKeyDown/OnKeyUp below (paired with TabStop=false on the buttons
    // above, so the form itself — not a Button, which only treats Space as input — holds focus).
    protected override bool IsInputKey(Keys keyData) => true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;

        var key = e.KeyCode;

        // Esc cancels only while nothing has been captured yet and no modifier
        // is currently held — otherwise Esc is a capturable key (e.g. Ctrl+Esc).
        if (key == Keys.Escape && _heldModifiers == KeyModifiers.None && _committed == null)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        if (TryMapModifier(key, out var mod))
            _heldModifiers |= mod;
        else
            _pendingMainKey = key;

        UpdatePreview();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;

        var key = e.KeyCode;

        if (TryMapModifier(key, out var mod))
        {
            _heldModifiers &= ~mod;
        }
        else if (_pendingMainKey == key)
        {
            _committed = new KeystrokeCombo(_heldModifiers, key);
            _pendingMainKey = null;
            _saveButton.Enabled = true;
        }

        UpdatePreview();
    }

    private static bool TryMapModifier(Keys key, out KeyModifiers mod)
    {
        mod = key switch
        {
            Keys.ControlKey or Keys.LControlKey or Keys.RControlKey => KeyModifiers.Control,
            Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey => KeyModifiers.Shift,
            Keys.Menu or Keys.LMenu or Keys.RMenu => KeyModifiers.Alt,
            Keys.LWin or Keys.RWin => KeyModifiers.Win,
            _ => KeyModifiers.None
        };
        return mod != KeyModifiers.None;
    }

    private void UpdatePreview()
    {
        if (_committed is { } c)
        {
            _previewLabel.Text = c.ToDisplayString();
            return;
        }

        var mods = KeystrokeCombo.FormatModifiers(_heldModifiers);
        _previewLabel.Text = _pendingMainKey is { } k
            ? $"{(mods.Length > 0 ? mods + "+" : "")}{k}"
            : (mods.Length > 0 ? mods + "+..." : "Press a key combination...");
    }
}
