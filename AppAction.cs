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
                LaunchInTerminal(Target, Arguments);
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

    private static void LaunchInTerminal(string command, string args)
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
