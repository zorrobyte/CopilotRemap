using System.Diagnostics;
using System.Text.Json.Serialization;

namespace CopilotRemap;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionType
{
    LaunchApp,
    LaunchStoreApp,
    RunInTerminal,
    OpenUrl
}

public sealed class AppAction
{
    public ActionType Type { get; init; }
    public string Target { get; init; } = "";
    public string Arguments { get; init; } = "";
    public string DisplayName { get; init; } = "";

    public void Execute()
    {
        switch (Type)
        {
            case ActionType.LaunchApp:
                Process.Start(new ProcessStartInfo
                {
                    FileName = Target,
                    Arguments = Arguments,
                    UseShellExecute = true
                });
                break;

            case ActionType.LaunchStoreApp:
                // Launch MSIX/Store apps via shell:AppsFolder\{AppUserModelId}
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"shell:AppsFolder\\{Target}",
                    UseShellExecute = false
                });
                break;

            case ActionType.RunInTerminal:
                LaunchInTerminal(Target, Arguments);
                break;

            case ActionType.OpenUrl:
                Process.Start(new ProcessStartInfo
                {
                    FileName = Target,
                    UseShellExecute = true
                });
                break;
        }
    }

    private static void LaunchInTerminal(string command, string args)
    {
        var fullCommand = string.IsNullOrEmpty(args) ? command : $"{command} {args}";

        // wt.exe is an App Execution Alias and can't be launched directly
        // with UseShellExecute=false, but we need UseShellExecute=false to
        // clear CLAUDECODE env var (blocks nested Claude Code sessions).
        // Solution: use cmd.exe as a shim to resolve the alias.
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c wt.exe new-tab -- {fullCommand}",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.Environment.Remove("CLAUDECODE");

        try
        {
            Process.Start(psi);
        }
        catch
        {
            // Fall back to PowerShell if Windows Terminal is not available
            var fallback = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoExit -Command \"& {fullCommand}\"",
                UseShellExecute = false
            };
            fallback.Environment.Remove("CLAUDECODE");
            Process.Start(fallback);
        }
    }

    // --- Presets ---

    public static AppAction ClaudeCode() => new()
    {
        Type = ActionType.RunInTerminal,
        Target = "claude",
        DisplayName = "Claude Code (Terminal)"
    };

    public static AppAction ClaudeDesktop()
    {
        var appId = FindClaudeDesktopAppId();
        var exePath = FindClaudeDesktopExe();
        if (appId != null)
        {
            return new AppAction
            {
                Type = ActionType.LaunchStoreApp,
                Target = appId,
                DisplayName = "Claude Desktop"
            };
        }
        else if (exePath != null)
        {
            return new AppAction
            {
                Type = ActionType.LaunchApp,
                Target = exePath,
                DisplayName = "Claude Desktop"
            };
        }
        else
        {
            return new AppAction
            {
                Type = ActionType.LaunchApp,
                Target = "",
                DisplayName = "Claude Desktop (Not Found)"
            };
        }
    }

    public static AppAction ClaudeWeb() => new()
    {
        Type = ActionType.OpenUrl,
        Target = "https://claude.ai",
        DisplayName = "claude.ai (Browser)"
    };

    public static bool IsClaudeDesktopInstalled() => FindClaudeDesktopAppId() != null || FindClaudeDesktopExe() != null;

    private static string? FindClaudeDesktopAppId()
    {
        try
        {
            // Query for the MSIX package
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"(Get-AppxPackage *Claude*).PackageFamilyName\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd().Trim();
            proc?.WaitForExit();

            if (!string.IsNullOrEmpty(output))
                return $"{output}!Claude";
        }
        catch { }

        return null;
    }

    // Looks for claude.exe in common install locations
    private static string? FindClaudeDesktopExe()
    {
        try
        {
            // User-local install (default for Claude Desktop)
            var userPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AnthropicClaude", "claude.exe");
            if (System.IO.File.Exists(userPath))
                return userPath;

            // Add more locations if needed
        }
        catch { }
        return null;
    }
}
