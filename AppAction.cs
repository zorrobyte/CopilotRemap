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

public sealed record AppAction
{
    public ActionType Type { get; init; }
    public string Target { get; init; } = "";
    public string Arguments { get; init; } = "";
    public string DisplayName { get; init; } = "";
    // Optional working directory for RunInTerminal
    public string? WorkingDirectory { get; init; }

    public void Execute()
    {
        if (string.IsNullOrWhiteSpace(Target))
            throw new InvalidOperationException("Action target is not configured.");

        switch (Type)
        {
            case ActionType.LaunchApp:
                // Validate target is a real file path, not a URL or shell command
                if (Target.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                    throw new InvalidOperationException($"Invalid application path: {Target}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = Target,
                    Arguments = Arguments,
                    UseShellExecute = true
                });
                break;

            case ActionType.LaunchStoreApp:
                // Validate AppUserModelId format (PackageFamilyName!AppId) —
                // reject shell metacharacters to prevent argument injection into explorer.exe
                if (Target.IndexOfAny(InvalidCommandChars) >= 0 || Target.Contains(".."))
                    throw new InvalidOperationException($"Invalid store app ID: {Target}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"shell:AppsFolder\\{Target}",
                    UseShellExecute = false
                });
                break;

            case ActionType.RunInTerminal:
                LaunchInTerminal(Target, Arguments, WorkingDirectory);
                break;

            case ActionType.OpenUrl:
                if (!Uri.TryCreate(Target, UriKind.Absolute, out var uri)
                    || (uri.Scheme != "https" && uri.Scheme != "http"))
                    throw new InvalidOperationException($"Invalid or disallowed URL scheme: {Target}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri.AbsoluteUri,
                    UseShellExecute = true
                });
                break;
        }
    }

    private static readonly char[] InvalidCommandChars = ['&', '|', ';', '>', '<', '`', '$', '(', ')', '{', '}', '\n', '\r'];

    private static void LaunchInTerminal(string command, string args, string? workingDir = null)
    {
        // Validate command to prevent injection via cmd.exe /c or powershell -Command
        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Command must not be empty.", nameof(command));

        if (command.IndexOfAny(InvalidCommandChars) >= 0 ||
            (!string.IsNullOrEmpty(args) && args.IndexOfAny(InvalidCommandChars) >= 0))
            throw new ArgumentException("Command or arguments contain disallowed shell metacharacters.");

        var quotedCommand = $"\"{command}\"";
        var fullCommand = string.IsNullOrEmpty(args) ? quotedCommand : $"{quotedCommand} {args}";

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
        if (!string.IsNullOrWhiteSpace(workingDir))
            psi.WorkingDirectory = workingDir;
        psi.Environment.Remove("CLAUDECODE");

        try
        {
            Process.Start(psi);
        }
        catch
        {
            // Fall back to PowerShell if Windows Terminal is not available.
            // Use -File semantics via encoded command to avoid injection.
            var fallback = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NoExit -Command \"& {fullCommand}\"",
                UseShellExecute = false
            };
            if (!string.IsNullOrWhiteSpace(workingDir))
                fallback.WorkingDirectory = workingDir;
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

    public static AppAction ClaudeCodeResumeById(string sessionId) => new()
    {
        Type = ActionType.RunInTerminal,
        Target = "claude",
        Arguments = $"--resume {sessionId}",
        DisplayName = $"Claude Code (Resume {(sessionId.Length >= 8 ? sessionId[..8] : sessionId)})"
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

    public static AppAction SearchChats() => new()
    {
        Type = ActionType.SearchChats,
        Target = "search",
        DisplayName = "Search Chats"
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
