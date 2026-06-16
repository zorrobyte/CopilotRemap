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

        // Create the tray icon first so menu-item callbacks can safely capture it.
        _trayIcon = new NotifyIcon
        {
            Icon = IconHelper.CreateTrayIcon(),
            Text = "CopilotRemap",
            Visible = true
        };

        // Build submenus
        var tapMenu = BuildActionSubmenu("Tap Action", _config.SingleTap, action => SetGestureAction("singleTap", action));
        var doubleTapMenu = BuildActionSubmenu("Double Tap Action", _config.DoubleTap, action => SetGestureAction("doubleTap", action));
        var holdMenu = BuildActionSubmenu("Hold Action", _config.Hold, action => SetGestureAction("hold", action));

        // Add menu item for default working directory
        var setWorkingDirItem = new ToolStripMenuItem("Set Default Working Directory...");
        setWorkingDirItem.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select default working directory for terminal and app actions",
                UseDescriptionForTitle = true,
                SelectedPath = _config.WorkingDirectory ?? string.Empty
            };
            if (dialog.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                _config = _config with { WorkingDirectory = dialog.SelectedPath };
                SaveConfig(_config);
                _trayIcon.ShowBalloonTip(2000, "CopilotRemap", $"Default working directory set to:\n{dialog.SelectedPath}", ToolTipIcon.Info);
            }
        };

        _trayIcon.ContextMenuStrip = new ContextMenuStrip
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
                setWorkingDirItem,
                _startupItem,
                new ToolStripSeparator(),
                new ToolStripMenuItem("Exit", null, (_, _) => Exit())
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
        claudeCodeItem.Click += (_, _) =>
        {
            // Use the configured working directory if present
            var action = AppAction.ClaudeCode();
            if (!string.IsNullOrWhiteSpace(_config.WorkingDirectory))
                action = action with { WorkingDirectory = _config.WorkingDirectory };
            onSet(action);
        };

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
            if (action != null)
            {
                // Ask if user wants to set a working directory
                using var dialog = new FolderBrowserDialog
                {
                    Description = "Select working directory for this application (optional)",
                    UseDescriptionForTitle = true
                };
                if (dialog.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                {
                    action = action with { WorkingDirectory = dialog.SelectedPath };
                }
                onSet(action);
            }
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

    private static readonly char[] DisallowedCommandChars = ['&', '|', ';', '>', '<', '`', '$', '(', ')', '{', '}', '\n', '\r'];

    private static AppAction? PromptCustomCommand()
    {
        using var dialog = new InputDialog(
            "Custom Command",
            "Command to run in terminal (e.g. python, node, wsl):",
            "");

        if (dialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.Value)) return null;

        var command = dialog.Value.Trim();

        if (command.IndexOfAny(DisallowedCommandChars) >= 0)
        {
            MessageBox.Show("Command contains disallowed characters (&, |, ;, >, <, etc.).",
                "Invalid Command", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        return new AppAction
        {
            Type = ActionType.RunInTerminal,
            Target = command,
            DisplayName = $"{command} (Terminal)"
        };
    }

    private static AppAction? PromptCustomUrl()
    {
        using var dialog = new InputDialog(
            "Custom URL",
            "URL to open in browser (https:// only):",
            "https://");

        if (dialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.Value)) return null;

        var url = dialog.Value.Trim();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUri)
            || (parsedUri.Scheme != "https" && parsedUri.Scheme != "http"))
        {
            MessageBox.Show("Only http:// and https:// URLs are allowed.",
                "Invalid URL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        return new AppAction
        {
            Type = ActionType.OpenUrl,
            Target = parsedUri.AbsoluteUri,
            DisplayName = parsedUri.Host
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

        ShowQuickLaunch();
    }

    private QuickLaunchWindow? _quickLaunch;

    private void ShowQuickLaunch()
    {
        // If an overlay is already open, just bring it forward instead of stacking.
        if (_quickLaunch is { IsDisposed: false })
        {
            ForceForeground(_quickLaunch);
            return;
        }

        var mainItems = new List<QuickLaunchWindow.LaunchItem>
        {
            // The overlay intercepts this exact title and switches to Resume mode
            // (full-text search over past conversations) instead of executing.
            new("Resume Chat…", "Search past conversations", _ => { }),
            new("Continue Last Session", "claude --continue",
                _ => LaunchClaudeAction(AppAction.ClaudeCodeContinue())),
            new("Claude Desktop", "Open the desktop app",
                _ => LaunchClaudeAction(AppAction.ClaudeDesktop())),
            new("claude.ai (Browser)", "Open in your browser",
                _ => LaunchClaudeAction(AppAction.ClaudeWeb())),
        };

        var win = new QuickLaunchWindow(
            mainItems,
            askAction: prompt =>
            {
                // Start a fresh Claude Code session; the typed prompt goes to the
                // clipboard so the user can paste it into the new terminal.
                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    try { Clipboard.SetText(prompt); } catch { }
                }
                LaunchClaudeAction(AppAction.ClaudeCode());
            },
            resumeByIdAction: id => LaunchClaudeAction(AppAction.ClaudeCodeResumeById(id)));

        _quickLaunch = win;
        win.FormClosed += (_, _) => _quickLaunch = null;
        win.Show();
        ForceForeground(win);
    }

    private void LaunchClaudeAction(AppAction action)
    {
        // Apply the configured default working directory to terminal launches.
        if (action.Type == ActionType.RunInTerminal
            && !string.IsNullOrWhiteSpace(_config.WorkingDirectory))
            action = action with { WorkingDirectory = _config.WorkingDirectory };

        try
        {
            action.Execute();
        }
        catch (Exception ex)
        {
            _trayIcon.ShowBalloonTip(3000, "CopilotRemap", ex.Message, ToolTipIcon.Error);
        }
    }

    // --- Win32: pull our overlay to the foreground from the tray process ---

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    private static void ForceForeground(Form form)
    {
        // A background/tray process can't normally steal focus; briefly attach to
        // the current foreground thread so SetForegroundWindow is honored.
        var fgHwnd = GetForegroundWindow();
        var fgThread = GetWindowThreadProcessId(fgHwnd, out _);
        var ourThread = GetCurrentThreadId();

        bool attached = false;
        if (fgThread != ourThread)
            attached = AttachThreadInput(ourThread, fgThread, true);

        SetForegroundWindow(form.Handle);
        form.Activate();

        if (attached)
            AttachThreadInput(ourThread, fgThread, false);
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
            ShowQuickLaunch();
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
        // Use -Command with properly escaped parameters instead of writing a temp script file.
        // This avoids TOCTOU race conditions on the temp file and reduces injection surface.
        var psCommand =
            "$ws = New-Object -ComObject WScript.Shell; " +
            $"$s = $ws.CreateShortcut([System.Management.Automation.WildcardPattern]::Escape('{EscapePowerShellString(shortcutPath)}')); " +
            $"$s.TargetPath = '{EscapePowerShellString(targetPath)}'; " +
            $"$s.Arguments = '{EscapePowerShellString(arguments)}'; " +
            "$s.Save()";

        var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand.Replace("\"", "\\\"")}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        });
        proc?.WaitForExit();
    }

    /// <summary>
    /// Escapes a string for safe inclusion in a PowerShell single-quoted string.
    /// </summary>
    private static string EscapePowerShellString(string value) => value.Replace("'", "''");

    // --- Config persistence ---

    private record CopilotConfig
    {
        public AppAction? SingleTap { get; init; }
        public AppAction? DoubleTap { get; init; }
        public AppAction? Hold { get; init; }
        public int DoubleTapDelayMs { get; init; } = 350;
        public int HoldDelayMs { get; init; } = 500;

        // Optional: default working directory for Claude Code (terminal)
        public string? WorkingDirectory { get; init; }
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
