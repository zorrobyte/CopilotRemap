using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CopilotRemap;

public sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly KeyboardHook _hook;
    private readonly ToolStripMenuItem _startupItem;

    // Header labels showing current assignments
    private readonly ToolStripMenuItem _tapLabel;
    private readonly ToolStripMenuItem _doubleTapLabel;
    private readonly ToolStripMenuItem _holdLabel;

    // Config
    private CopilotConfig _config;

    // Gesture state
    private int _tapCount;
    private bool _holdFired;
    private bool _keyIsDown;
    private readonly System.Windows.Forms.Timer _doubleTapTimer;
    private readonly System.Windows.Forms.Timer _holdTimer;



    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CopilotRemap");
    private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");

    public TrayApp()
    {
        _config = LoadConfig();

        // Gesture timers
        _doubleTapTimer = new System.Windows.Forms.Timer { Interval = _config.DoubleTapDelayMs };
        _doubleTapTimer.Tick += (_, _) =>
        {
            _doubleTapTimer.Stop();
            ExecuteAction(_config.SingleTap, "Tap");
            ResetGestureState();
        };

        _holdTimer = new System.Windows.Forms.Timer { Interval = _config.HoldDelayMs };
        _holdTimer.Tick += (_, _) =>
        {
            _holdTimer.Stop();
            if (_keyIsDown)
            {
                _holdFired = true;
                ExecuteAction(_config.Hold, "Hold");
            }
        };

        // Header labels
        _tapLabel = new ToolStripMenuItem($"Tap:    {_config.SingleTap?.DisplayName ?? "(none)"}")
            { Enabled = false, Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold) };
        _doubleTapLabel = new ToolStripMenuItem($"2x Tap: {_config.DoubleTap?.DisplayName ?? "(none)"}")
            { Enabled = false, Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold) };
        _holdLabel = new ToolStripMenuItem($"Hold:   {_config.Hold?.DisplayName ?? "(none)"}")
            { Enabled = false, Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold) };

        _startupItem = new ToolStripMenuItem("Run at Startup")
        {
            Checked = IsInStartup(),
            CheckOnClick = true
        };
        _startupItem.Click += (_, _) => ToggleStartup(_startupItem.Checked);

        // Build submenus
        var tapMenu = BuildActionSubmenu("Tap Action", _config.SingleTap, action => SetGestureAction("singleTap", action));
        var doubleTapMenu = BuildActionSubmenu("Double Tap Action", _config.DoubleTap, action => SetGestureAction("doubleTap", action));
        var holdMenu = BuildActionSubmenu("Hold Action", _config.Hold, action => SetGestureAction("hold", action));

        _trayIcon = new NotifyIcon
        {
            Icon = IconHelper.CreateTrayIcon(),
            Text = "CopilotRemap",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip
            {
                Items =
                {
                    _tapLabel,
                    _doubleTapLabel,
                    _holdLabel,
                    new ToolStripSeparator(),
                    tapMenu,
                    doubleTapMenu,
                    holdMenu,
                    new ToolStripSeparator(),
                    _startupItem,
                    new ToolStripSeparator(),
                    new ToolStripMenuItem("Exit", null, (_, _) => Exit())
                }
            }
        };

        _hook = new KeyboardHook();
        _hook.CopilotKeyDown += OnCopilotKeyDown;
        _hook.CopilotKeyUp += OnCopilotKeyUp;
        _hook.CopilotSpacePressed += OnCopilotSpacePressed;
        _hook.Install();
    }

    // --- Submenu builder ---

    private ToolStripMenuItem BuildActionSubmenu(string label, AppAction? current, Action<AppAction?> onSet)
    {
        var menu = new ToolStripMenuItem(label);

        // Presets
        var claudeCodeItem = new ToolStripMenuItem("Claude Code (Terminal)");
        claudeCodeItem.Click += (_, _) => onSet(AppAction.ClaudeCode());

        var claudeDesktopItem = new ToolStripMenuItem("Claude Desktop");
        claudeDesktopItem.Click += (_, _) => onSet(AppAction.ClaudeDesktop());
        if (!AppAction.IsClaudeDesktopInstalled())
        {
            claudeDesktopItem.Text += " (not found)";
            claudeDesktopItem.Enabled = false;
        }

        var claudeWebItem = new ToolStripMenuItem("claude.ai (Browser)");
        claudeWebItem.Click += (_, _) => onSet(AppAction.ClaudeWeb());

        var searchChatsItem = new ToolStripMenuItem("Search Chats");
        searchChatsItem.Click += (_, _) => onSet(AppAction.SearchChats());
        if (!AppAction.IsClaudeDesktopInstalled())
        {
            searchChatsItem.Text += " (needs Claude Desktop)";
            searchChatsItem.Enabled = false;
        }

        // Custom options
        var customAppItem = new ToolStripMenuItem("Custom Application...");
        customAppItem.Click += (_, _) =>
        {
            var action = PromptCustomApp();
            if (action != null) onSet(action);
        };

        var customCmdItem = new ToolStripMenuItem("Custom Command...");
        customCmdItem.Click += (_, _) =>
        {
            var action = PromptCustomCommand();
            if (action != null) onSet(action);
        };

        var customUrlItem = new ToolStripMenuItem("Custom URL...");
        customUrlItem.Click += (_, _) =>
        {
            var action = PromptCustomUrl();
            if (action != null) onSet(action);
        };

        var noneItem = new ToolStripMenuItem("None (disable)");
        noneItem.Click += (_, _) => onSet(null);

        // Checkmarks
        var presets = new[] { claudeCodeItem, claudeDesktopItem, claudeWebItem, searchChatsItem };
        SetCheckmark(presets, noneItem, current);

        menu.DropDownItems.AddRange(new ToolStripItem[]
        {
            claudeCodeItem, claudeDesktopItem, claudeWebItem, searchChatsItem,
            new ToolStripSeparator(),
            customAppItem, customCmdItem, customUrlItem,
            new ToolStripSeparator(),
            noneItem
        });

        return menu;
    }

    private static void SetCheckmark(ToolStripMenuItem[] presets, ToolStripMenuItem noneItem, AppAction? current)
    {
        foreach (var p in presets) p.Checked = false;
        noneItem.Checked = false;

        if (current == null)
        {
            noneItem.Checked = true;
            return;
        }

        var match = presets.FirstOrDefault(p =>
            (p.Text ?? "").Replace(" (not found)", "") == current.DisplayName);
        if (match != null)
            match.Checked = true;
    }

    // --- Set gesture action ---

    private void SetGestureAction(string gesture, AppAction? action)
    {
        switch (gesture)
        {
            case "singleTap":
                _config = _config with { SingleTap = action };
                _tapLabel.Text = $"Tap:    {action?.DisplayName ?? "(none)"}";
                break;
            case "doubleTap":
                _config = _config with { DoubleTap = action };
                _doubleTapLabel.Text = $"2x Tap: {action?.DisplayName ?? "(none)"}";
                break;
            case "hold":
                _config = _config with { Hold = action };
                _holdLabel.Text = $"Hold:   {action?.DisplayName ?? "(none)"}";
                break;
        }

        SaveConfig(_config);
        RebuildSubmenus();

        var name = action?.DisplayName ?? "None";
        _trayIcon.ShowBalloonTip(2000, "CopilotRemap",
            $"{GestureDisplayName(gesture)} → {name}", ToolTipIcon.Info);
    }

    private static string GestureDisplayName(string gesture) => gesture switch
    {
        "singleTap" => "Tap",
        "doubleTap" => "Double Tap",
        "hold" => "Hold",
        _ => gesture
    };

    private void RebuildSubmenus()
    {
        var strip = _trayIcon.ContextMenuStrip!;

        // Find and replace the three submenu items (indices 4, 5, 6 after header labels + separator)
        ReplaceSubmenuAt(strip, 4, "Tap Action", _config.SingleTap, a => SetGestureAction("singleTap", a));
        ReplaceSubmenuAt(strip, 5, "Double Tap Action", _config.DoubleTap, a => SetGestureAction("doubleTap", a));
        ReplaceSubmenuAt(strip, 6, "Hold Action", _config.Hold, a => SetGestureAction("hold", a));
    }

    private void ReplaceSubmenuAt(ContextMenuStrip strip, int index, string label, AppAction? current, Action<AppAction?> onSet)
    {
        strip.Items.RemoveAt(index);
        strip.Items.Insert(index, BuildActionSubmenu(label, current, onSet));
    }

    // --- Custom action dialogs ---

    private static AppAction? PromptCustomApp()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select Application",
            Filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };
        if (dialog.ShowDialog() != DialogResult.OK) return null;

        return new AppAction
        {
            Type = ActionType.LaunchApp,
            Target = dialog.FileName,
            DisplayName = Path.GetFileNameWithoutExtension(dialog.FileName)
        };
    }

    private static AppAction? PromptCustomCommand()
    {
        using var dialog = new InputDialog(
            "Custom Command",
            "Command to run in terminal (e.g. python, node, wsl):",
            "");

        if (dialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.Value)) return null;

        return new AppAction
        {
            Type = ActionType.RunInTerminal,
            Target = dialog.Value.Trim(),
            DisplayName = $"{dialog.Value.Trim()} (Terminal)"
        };
    }

    private static AppAction? PromptCustomUrl()
    {
        using var dialog = new InputDialog(
            "Custom URL",
            "URL to open in browser:",
            "https://");

        if (dialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.Value)) return null;

        var url = dialog.Value.Trim();
        return new AppAction
        {
            Type = ActionType.OpenUrl,
            Target = url,
            DisplayName = new Uri(url).Host
        };
    }

    // --- Gesture detection ---

    private void OnCopilotKeyDown()
    {
        if (_keyIsDown) return; // Ignore key repeat
        _keyIsDown = true;

        _tapCount++;
        _doubleTapTimer.Stop();
        _holdTimer.Start();
    }

    private void OnCopilotKeyUp()
    {
        _keyIsDown = false;
        _holdTimer.Stop();

        if (_holdFired)
        {
            ResetGestureState();
            return;
        }

        if (_tapCount >= 2)
        {
            ExecuteAction(_config.DoubleTap, "Double Tap");
            ResetGestureState();
        }
        else if (_tapCount == 1)
        {
            _doubleTapTimer.Start();
        }
    }

    private void OnCopilotSpacePressed()
    {
        // Cancel any in-progress gesture so key-up becomes a no-op
        ResetGestureState();
        _keyIsDown = false;

        TriggerClaudeSearch();
    }

    private void TriggerClaudeSearch()
    {
        // Find existing Claude Desktop window
        var hwnd = FindClaudeWindow();

        if (hwnd == IntPtr.Zero)
        {
            // Not running — launch it, then poll for the window
            try
            {
                AppAction.ClaudeDesktop().Execute();
            }
            catch (Exception ex)
            {
                _trayIcon.ShowBalloonTip(3000, "CopilotRemap",
                    $"Claude Desktop not found: {ex.Message}", ToolTipIcon.Error);
                return;
            }

            // Poll until window appears (up to 5s in 100ms steps)
            int retries = 50;
            var pollTimer = new System.Windows.Forms.Timer { Interval = 100 };
            pollTimer.Tick += (_, _) =>
            {
                hwnd = FindClaudeWindow();
                if (hwnd != IntPtr.Zero || --retries <= 0)
                {
                    pollTimer.Stop();
                    pollTimer.Dispose();
                    if (hwnd != IntPtr.Zero)
                        FocusAndSearch(hwnd);
                }
            };
            pollTimer.Start();
        }
        else
        {
            FocusAndSearch(hwnd);
        }
    }

    // --- Win32: find and focus Claude Desktop ---

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const int SW_RESTORE = 9;

    private static IntPtr FindClaudeWindow()
    {
        IntPtr found = IntPtr.Zero;
        var sb = new System.Text.StringBuilder(256);

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();
            if (title.Contains("Claude", StringComparison.OrdinalIgnoreCase)
                && !title.Contains("CopilotRemap", StringComparison.OrdinalIgnoreCase))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        return found;
    }

    private void FocusAndSearch(IntPtr hwnd)
    {
        // Restore if minimized
        if (IsIconic(hwnd))
            ShowWindow(hwnd, SW_RESTORE);

        // Attach to foreground thread to reliably call SetForegroundWindow
        var fgHwnd = GetForegroundWindow();
        var fgThread = GetWindowThreadProcessId(fgHwnd, out _);
        var ourThread = GetCurrentThreadId();

        bool attached = false;
        if (fgThread != ourThread)
            attached = AttachThreadInput(ourThread, fgThread, true);

        SetForegroundWindow(hwnd);

        if (attached)
            AttachThreadInput(ourThread, fgThread, false);

        // Wait for window activation + user releasing Copilot keys, then SendKeys
        var t = new System.Windows.Forms.Timer { Interval = 200 };
        t.Tick += (_, _) =>
        {
            t.Stop();
            t.Dispose();
            // Escape first to close search if already open (Ctrl+K is a toggle),
            // then Ctrl+K to open it fresh — guarantees search is always shown.
            SendKeys.Send("{ESC}");
            SendKeys.Send("^k");
        };
        t.Start();
    }

    private void ResetGestureState()
    {
        _tapCount = 0;
        _holdFired = false;
        _doubleTapTimer.Stop();
        _holdTimer.Stop();
    }

    // --- Execute action ---

    private void ExecuteAction(AppAction? action, string gestureName)
    {
        if (action == null || string.IsNullOrEmpty(action.Target))
        {
            _trayIcon.ShowBalloonTip(3000, "CopilotRemap",
                $"No action configured for {gestureName}. Right-click the tray icon to set one.",
                ToolTipIcon.Warning);
            return;
        }

        if (action.Type == ActionType.SearchChats)
        {
            TriggerClaudeSearch();
            return;
        }

        if (action.Type == ActionType.LaunchApp && !File.Exists(action.Target))
        {
            _trayIcon.ShowBalloonTip(3000, "CopilotRemap",
                $"Target not found: {action.Target}", ToolTipIcon.Error);
            return;
        }

        try
        {
            action.Execute();
        }
        catch (Exception ex)
        {
            _trayIcon.ShowBalloonTip(3000, "CopilotRemap",
                $"Failed: {ex.Message}", ToolTipIcon.Error);
        }
    }

    // --- Lifecycle ---

    private void Exit()
    {
        _hook.Dispose();
        _doubleTapTimer.Dispose();
        _holdTimer.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
    }

    // --- Startup shortcut via shell:startup ---

    private static string StartupShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "CopilotRemap.lnk");

    private static bool IsInStartup() => File.Exists(StartupShortcutPath);

    private static void ToggleStartup(bool enable)
    {
        if (enable)
        {
            string targetPath;
            string arguments = "";
            var processPath = Environment.ProcessPath ?? "";

            if (processPath.EndsWith("CopilotRemap.exe", StringComparison.OrdinalIgnoreCase))
            {
                targetPath = processPath;
            }
            else
            {
                targetPath = processPath;
                arguments = $"\"{Path.Combine(AppContext.BaseDirectory, "CopilotRemap.dll")}\"";
            }

            CreateShortcut(StartupShortcutPath, targetPath, arguments);
        }
        else if (File.Exists(StartupShortcutPath))
        {
            File.Delete(StartupShortcutPath);
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string arguments)
    {
        var script = Path.Combine(Path.GetTempPath(), "CopilotRemap_mklink.ps1");
        File.WriteAllText(script,
            $"$ws = New-Object -ComObject WScript.Shell\n" +
            $"$s = $ws.CreateShortcut('{shortcutPath.Replace("'", "''")}')\n" +
            $"$s.TargetPath = '{targetPath.Replace("'", "''")}'\n" +
            $"$s.Arguments = '{arguments.Replace("'", "''")}'\n" +
            $"$s.Save()\n");

        var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        });
        proc?.WaitForExit();

        try { File.Delete(script); } catch { }
    }

    // --- Config persistence ---

    private record CopilotConfig
    {
        public AppAction? SingleTap { get; init; }
        public AppAction? DoubleTap { get; init; }
        public AppAction? Hold { get; init; }
        public int DoubleTapDelayMs { get; init; } = 350;
        public int HoldDelayMs { get; init; } = 500;
    }

    private static CopilotConfig LoadConfig()
    {
        if (!File.Exists(ConfigFile))
            return new CopilotConfig();

        try
        {
            var json = File.ReadAllText(ConfigFile);

            // Try new config format first
            var config = JsonSerializer.Deserialize<CopilotConfig>(json, JsonOpts);
            if (config != null && (config.SingleTap != null || config.DoubleTap != null || config.Hold != null))
                return config;

            // Backwards compatibility: old single-action config migrates to SingleTap
            var legacy = JsonSerializer.Deserialize<AppAction>(json);
            if (legacy != null && !string.IsNullOrEmpty(legacy.Target))
            {
                var migrated = new CopilotConfig { SingleTap = legacy };
                SaveConfig(migrated);
                return migrated;
            }
        }
        catch { }

        return new CopilotConfig();
    }

    private static void SaveConfig(CopilotConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config, JsonOpts);
        File.WriteAllText(ConfigFile, json);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
