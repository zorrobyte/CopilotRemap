using System.Diagnostics;
using System.Text.Json.Serialization;

namespace CopilotRemap;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionType
{
    LaunchApp,
    LaunchStoreApp,
    RunInTerminal,
    OpenUrl,
    SearchChats
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

    public static AppAction ClaudeCodeContinue() => new()
    {
        Type = ActionType.RunInTerminal,
        Target = "claude",
        Arguments = "--continue",
        DisplayName = "Claude Code (Continue)"
    };

    public static AppAction ClaudeCodeResume() => new()
    {
        Type = ActionType.RunInTerminal,
        Target = "claude",
        Arguments = "--resume",
        DisplayName = "Claude Code (Resume)"
    };

    public static AppAction ClaudeCodeResumeById(string sessionId) => new()
    {
        Type = ActionType.RunInTerminal,
        Target = "claude",
        Arguments = $"--resume {sessionId}",
        DisplayName = $"Claude Code (Resume {sessionId[..8]})"
    };

    public static AppAction ClaudeDesktop()
    {
        var appId = FindClaudeDesktopAppId();
        return new AppAction
        {
            Type = appId != null ? ActionType.LaunchStoreApp : ActionType.LaunchApp,
            Target = appId ?? "",
            DisplayName = "Claude Desktop"
        };
    }

    public static AppAction ClaudeWeb() => new()
    {
        Type = ActionType.OpenUrl,
        Target = "https://claude.ai",
        DisplayName = "claude.ai (Browser)"
    };

    public static AppAction SearchChats() => new()
    {
        Type = ActionType.SearchChats,
        Target = "search",
        DisplayName = "Search Chats"
    };

    public static bool IsClaudeDesktopInstalled() => FindClaudeDesktopAppId() != null;

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
}
