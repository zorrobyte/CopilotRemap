using System.Text;
using System.Text.Json;

namespace CopilotRemap;

/// <summary>
/// Scans Claude Code conversation files (~/.claude/projects/) and extracts
/// session metadata + full-text content for the Resume search feature.
/// </summary>
public static class ChatSessionScanner
{
    public record ChatSession(
        string SessionId,
        string Project,
        string Slug,
        string FirstMessage,
        string Cwd,
        DateTime LastModified,
        string SearchableText);

    private static readonly string ProjectsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "projects");

    /// <summary>
    /// Scans all Claude Code session files and returns metadata sorted by last modified (newest first).
    /// Indexes the full conversation text (user messages + assistant replies) for search.
    /// Skips subagent conversations.
    /// </summary>
    public static List<ChatSession> Scan()
    {
        var sessions = new List<ChatSession>();

        if (!Directory.Exists(ProjectsDir))
            return sessions;

        foreach (var file in Directory.EnumerateFiles(ProjectsDir, "*.jsonl", SearchOption.AllDirectories))
        {
            // Skip subagent files — they're implementation details of a parent session
            if (file.Contains("subagents", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var session = ParseSession(file);
                if (session != null)
                    sessions.Add(session);
            }
            catch { } // Skip unreadable files
        }

        sessions.Sort((a, b) => b.LastModified.CompareTo(a.LastModified));
        return sessions;
    }

    private static ChatSession? ParseSession(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length == 0) return null;

        string? sessionId = null;
        string? slug = null;
        string? firstUserMessage = null;
        string? cwd = null;
        var searchText = new StringBuilder(512);

        foreach (var line in File.ReadLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (root.TryGetProperty("sessionId", out var sid))
                    sessionId ??= sid.GetString();

                if (root.TryGetProperty("slug", out var s) && s.GetString() is { Length: > 0 } slugVal)
                    slug ??= slugVal;

                if (root.TryGetProperty("cwd", out var c) && c.GetString() is { Length: > 0 } cwdVal)
                    cwd ??= cwdVal;

                if (!root.TryGetProperty("type", out var typeProp)) continue;
                var type = typeProp.GetString();

                // Index user messages
                if (type == "user"
                    && root.TryGetProperty("message", out var msg)
                    && msg.TryGetProperty("role", out var role) && role.GetString() == "user"
                    && msg.TryGetProperty("content", out var content))
                {
                    var text = ExtractText(content);
                    if (text.Length > 0)
                    {
                        firstUserMessage ??= text.Length > 200 ? text[..200] : text;
                        AppendSearchText(searchText, text);
                    }
                }

                // Index assistant text replies (skip tool_use blocks, keep text blocks)
                if (type == "assistant"
                    && root.TryGetProperty("message", out var aMsg)
                    && aMsg.TryGetProperty("content", out var aContent))
                {
                    if (aContent.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var block in aContent.EnumerateArray())
                        {
                            if (block.TryGetProperty("type", out var bt) && bt.GetString() == "text"
                                && block.TryGetProperty("text", out var textEl))
                            {
                                AppendSearchText(searchText, textEl.GetString() ?? "");
                            }
                        }
                    }
                    else if (aContent.ValueKind == JsonValueKind.String)
                    {
                        AppendSearchText(searchText, aContent.GetString() ?? "");
                    }
                }
            }
            catch { } // Skip malformed lines
        }

        if (sessionId == null) return null;

        // Derive project name from directory structure
        var projectDir = Path.GetFileName(Path.GetDirectoryName(filePath)) ?? "";
        var project = projectDir.Replace("--", "/").Replace("-", " ");
        if (project.Length > 1 && project[1] == '/')
            project = project[0] + ":" + project[1..]; // Restore drive letter: d/Projects → d:/Projects

        // Add metadata to searchable text
        searchText.Append(' ').Append(project).Append(' ').Append(slug ?? "");

        return new ChatSession(
            SessionId: sessionId,
            Project: project,
            Slug: slug ?? sessionId[..8],
            FirstMessage: firstUserMessage ?? "(empty conversation)",
            Cwd: cwd ?? "",
            LastModified: fileInfo.LastWriteTime,
            SearchableText: searchText.ToString());
    }

    private static string ExtractText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            var text = content.GetString() ?? "";
            // Strip XML-like tags (e.g. <local-command-caveat>)
            if (text.StartsWith('<'))
            {
                var closeIdx = text.IndexOf('>');
                if (closeIdx > 0) text = text[(closeIdx + 1)..];
            }
            return text.Trim();
        }

        // Content can be an array of blocks
        if (content.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var bt) && bt.GetString() == "text"
                    && block.TryGetProperty("text", out var textEl))
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(textEl.GetString() ?? "");
                }
            }
            return sb.ToString().Trim();
        }

        return "";
    }

    private static void AppendSearchText(StringBuilder sb, string text)
    {
        // Cap total indexed text to ~8KB per session to keep memory reasonable
        if (sb.Length > 8192) return;
        if (sb.Length > 0) sb.Append(' ');
        var toAppend = Math.Min(text.Length, 8192 - sb.Length);
        sb.Append(text, 0, toAppend);
    }
}
