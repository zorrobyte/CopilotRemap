# CopilotRemap

A lightweight Windows system tray utility that remaps the Copilot key on your keyboard to launch whatever you want.

No bloated apps, no PowerToys, no AutoHotkey — just a single, tiny .NET app that sits in your system tray and does exactly one thing: intercepts the Copilot key and runs your chosen action.

## Features

- **Three gesture types** — single tap, double tap, and press-and-hold, each independently configurable
- **Intercepts the Copilot key** — handles both `VK_LAUNCH_APP1` and `Win+Shift+F23` key mappings used by different keyboards
- **Built-in presets** — one-click setup for Claude Code, Claude Desktop, or claude.ai
- **Fully customizable** — launch any application, run any terminal command, or open any URL
- **System tray app** — runs silently in the background with a right-click menu
- **Run at startup** — optional toggle to launch automatically when you log in
- **Single instance** — prevents duplicate copies from running
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
| **Claude Code (Terminal)** | Opens `claude` in Windows Terminal |
| **Claude Desktop** | Launches the Claude Desktop app (auto-detects MSIX install) |
| **claude.ai (Browser)** | Opens claude.ai in your default browser |
| **Custom Application...** | File picker — choose any `.exe` |
| **Custom Command...** | Run any command in a terminal (e.g. `python`, `wsl`, `node`) |
| **Custom URL...** | Open any URL in your default browser |
| **None (disable)** | Disable this gesture |

4. Press the Copilot key on your keyboard — your chosen action fires based on the gesture

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
  "DoubleTapDelayMs": 350,
  "HoldDelayMs": 500
}
```

Old single-action configs are automatically migrated to the `SingleTap` gesture.

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
├── Program.cs          Entry point, single-instance mutex
├── TrayApp.cs          System tray icon, context menu, gesture detection, config
├── KeyboardHook.cs     Low-level keyboard hook (Win32 P/Invoke)
├── AppAction.cs        Action model, presets, Execute logic
├── InputDialog.cs      Minimal text input dialog
├── IconHelper.cs       Generates the tray icon at runtime via GDI+
├── CopilotRemap.csproj .NET 9 WinForms project
└── installer/
    └── CopilotRemap.iss     Inno Setup installer script
```

## License

[MIT](LICENSE)
