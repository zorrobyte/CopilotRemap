# CopilotRemap

A lightweight Windows system tray utility that remaps the Copilot key on your keyboard to launch whatever you want.

No bloated apps, no PowerToys, no AutoHotkey — just a single, tiny .NET app that sits in your system tray and does exactly one thing: intercepts the Copilot key and runs your chosen action.

## Features

- **Three gesture types** — single tap, double tap, and press-and-hold, each independently configurable
- **QuickLaunch overlay** — press **Copilot + Space** for a Spotlight-style launcher: type a prompt to start a new Claude Code chat, or pick Resume / Continue / Desktop / Web
- **Search & resume chats** — full-text search across your past Claude Code conversations and resume any one of them by session
- **Intercepts the Copilot key** — handles both `VK_LAUNCH_APP1` and `Win+Shift+F23` key mappings used by different keyboards
- **Built-in presets** — one-click setup for Claude Code, Claude Desktop, or claude.ai
- **Fully customizable** — launch any application, run any terminal command, open any URL, or send any keystroke combination
- **Synthetic keystrokes** — assign any key combo (e.g. Ctrl+C, Alt+F4, Win+D) to a gesture; record it by pressing the keys, no typing required
- **Default working directory** — set the folder Claude Code (and other terminal commands) launches in
- **System tray app** — runs silently in the background with a right-click menu
- **Run at startup** — optional toggle to launch automatically when you log in
- **Single instance** — prevents duplicate copies from running
- **Hardened by default** — validates URLs, application paths, and commands, and rejects shell metacharacters to prevent injection
- **Zero dependencies** — just .NET (already on Windows 11)

## Quick Start

### Install from Release

Download `CopilotRemap-Setup.exe` from the [latest release](https://github.com/Zorrobyte/CopilotRemap/releases/latest) and run it. The installer copies the app to `%LocalAppData%\CopilotRemap` and creates a Start Menu shortcut.

### Build from Source

```
dotnet build -c Release
```

The built executable will be at `bin/Release/net9.0-windows/CopilotRemap.exe`.

### Run

```
dotnet run
```

Or launch `CopilotRemap.exe` directly after building.

### Publish as a standalone exe

```
dotnet publish -c Release -r win-x64 --self-contained false -o publish
```

This produces a small `publish/CopilotRemap.exe` that you can put anywhere.

## Usage

1. Run the app — an indigo keycap icon appears in your system tray
2. Right-click the icon to open the menu
3. Configure actions for each gesture:

| Gesture | Default | Description |
|---|---|---|
| **Tap** | — | Single press and release of the Copilot key |
| **Double Tap** | — | Two quick presses within 350ms |
| **Hold** | — | Press and hold for 500ms |

Each gesture has its own submenu with these options:

| Menu Option | What it does |
|---|---|
| **Claude Code (Terminal)** | Opens `claude` in Windows Terminal (in your default working directory, if set) |
| **Claude Desktop** | Launches the Claude Desktop app (auto-detects MSIX or standalone `.exe` install) |
| **claude.ai (Browser)** | Opens claude.ai in your default browser |
| **Search Chats** | Opens the QuickLaunch overlay to search and resume past Claude Code conversations |
| **Custom Application...** | File picker — choose any `.exe` |
| **Custom Command...** | Run any command in a terminal (e.g. `python`, `wsl`, `node`) |
| **Custom URL...** | Open any URL in your default browser |
| **Custom Keystroke...** | Press-to-record dialog — captures any key combo and sends it via SendInput |
| **None (disable)** | Disable this gesture |

You can also set a **default working directory** from the tray menu (*Set Default Working Directory...*), which is applied to Claude Code and other terminal commands.

4. Press the Copilot key on your keyboard — your chosen action fires based on the gesture

### QuickLaunch Overlay

Press **Copilot + Space** at any time to open the QuickLaunch overlay — a Spotlight-style launcher with two modes:

- **Ask** — type a prompt and press **Enter** to start a new Claude Code chat with it, or choose **Continue Last Session** (`claude --continue`), **Claude Desktop**, or **claude.ai**
- **Resume** — pick **Resume Chat…** to full-text search across your past Claude Code conversations (indexed from `~/.claude/projects/`); use the arrow keys to select and **Enter** to resume that session

Press **Esc** to dismiss the overlay.

### Example Setup

- **Tap** → Claude Desktop
- **Double Tap** → Claude Code in Terminal
- **Hold** → claude.ai in Browser

## How It Works

CopilotRemap installs a low-level keyboard hook (`SetWindowsHookEx` with `WH_KEYBOARD_LL`) that intercepts key events before they reach any application. It tracks both key-down and key-up events to classify gestures:

- **Single tap**: Key released before the hold threshold, and no second tap within the double-tap window
- **Double tap**: Two key presses detected within the double-tap window (350ms)
- **Hold**: Key held down past the hold threshold (500ms) — fires immediately without waiting for release

The Copilot key on Windows keyboards sends one of two signals depending on the manufacturer:
- **`VK_LAUNCH_APP1`** (0xB6) — used by some keyboards as a direct virtual key
- **`Win+Shift+F23`** — used by others as a key combination

CopilotRemap handles both.

## Configuration

Settings are stored as JSON at:

```
%APPDATA%\CopilotRemap\config.json
```

Example config:
```json
{
  "SingleTap": {
    "Type": "LaunchStoreApp",
    "Target": "AnthropicPBC.Claude_xxxxx!Claude",
    "DisplayName": "Claude Desktop"
  },
  "DoubleTap": {
    "Type": "RunInTerminal",
    "Target": "claude",
    "DisplayName": "Claude Code (Terminal)"
  },
  "Hold": {
    "Type": "OpenUrl",
    "Target": "https://claude.ai",
    "DisplayName": "claude.ai (Browser)"
  },
  "WorkingDirectory": "C:\\Projects",
  "DoubleTapDelayMs": 350,
  "HoldDelayMs": 500
}
```

Old single-action configs are automatically migrated to the `SingleTap` gesture.

Keystroke actions store the combo as plain text (e.g. `"Target": "Ctrl+Shift+C"`), captured via the "Custom Keystroke..." recorder — no manual editing needed.

## Building from Source

### Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (or Visual Studio 2022 with .NET desktop workload)

### Build

```
git clone https://github.com/Zorrobyte/CopilotRemap.git
cd CopilotRemap
dotnet build
```

### Run

```
dotnet run
```

### Create Installer

Requires [Inno Setup 6](https://jrsoftware.org/isdl.php).

```
dotnet publish -c Release -r win-x64 --self-contained false -o publish
iscc installer\CopilotRemap.iss
```

This produces `installer\CopilotRemap-Setup.exe`.

## Project Structure

```
CopilotRemap/
├── Program.cs            Entry point, single-instance mutex
├── TrayApp.cs            System tray icon, context menu, gesture detection, config
├── KeyboardHook.cs       Low-level keyboard hook (Win32 P/Invoke)
├── AppAction.cs          Action model, presets, Execute logic
├── ChatSessionScanner.cs Indexes ~/.claude/projects/ for chat search & resume
├── QuickLaunchWindow.cs  Spotlight-style QuickLaunch / Resume overlay (Copilot+Space)
├── InputDialog.cs        Minimal text input dialog
├── KeystrokeCombo.cs     Keystroke combo model, serialization, pretty formatting
├── KeySender.cs          SendInput wrapper for synthetic key events
├── KeystrokeCaptureDialog.cs  Press-to-record dialog for capturing a key combo
├── IconHelper.cs         Generates the tray icon at runtime via GDI+
├── CopilotRemap.csproj   .NET 9 WinForms project
└── installer/
    └── CopilotRemap.iss     Inno Setup installer script
```

## License

[MIT](LICENSE)
