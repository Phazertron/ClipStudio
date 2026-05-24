using System;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ClipStudio.UI;

/// <summary>
/// Builds pre-filled GitHub Issues URLs for crash reports and user feedback.
/// Environment information is gathered automatically from the running process.
/// </summary>
public static class GitHubIssueHelper
{
    private const string IssueBaseUrl = "https://github.com/Phazertron/ClipStudio/issues/new";

    // GitHub URLs can handle ~8 000 chars; leave headroom for the template wrapper.
    private const int MaxStackTraceChars = 2500;

    /// <summary>
    /// Builds a GitHub new-issue URL for a crash report.
    /// The title includes the exception type and a UTC timestamp.
    /// The body embeds a structured template with the stack trace in a collapsible block.
    /// </summary>
    /// <param name="crashText">Full content of the crash dump file.</param>
    public static string BuildCrashIssueUrl(string crashText)
    {
        var exceptionType = ExtractExceptionType(crashText);
        var title = $"Crash: {exceptionType} — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC";

        var stackTrace = crashText.Length > MaxStackTraceChars
            ? crashText[..MaxStackTraceChars] + "\n[truncated — attach full log from %AppData%/ClipStudio/logs]"
            : crashText;

        var body =
            $"**What were you doing when the crash occurred?**\n" +
            $"<!-- Describe what you were doing in ClipStudio just before it crashed -->\n\n" +
            $"**Steps to reproduce (if known):**\n" +
            $"1. \n" +
            $"2. \n\n" +
            $"---\n\n" +
            $"**Environment (auto-detected):**\n" +
            $"{GatherEnvironmentInfo()}\n\n" +
            $"<details>\n" +
            $"<summary>Crash log (auto-attached)</summary>\n\n" +
            $"```\n{stackTrace}\n```\n\n" +
            $"</details>";

        return $"{IssueBaseUrl}?labels=bug&title={WebUtility.UrlEncode(title)}&body={WebUtility.UrlEncode(body)}";
    }

    /// <summary>
    /// Builds a GitHub new-issue URL for user-initiated feedback.
    /// The body contains a type-selector checklist and prompts for context.
    /// No label is pre-set since the type is unknown until the user selects one.
    /// </summary>
    public static string BuildFeedbackIssueUrl()
    {
        var title = $"Feedback — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC";

        var body =
            $"**Type:** (check one)\n" +
            $"- [ ] Bug report\n" +
            $"- [ ] Feature request\n" +
            $"- [ ] General feedback\n\n" +
            $"**Description:**\n" +
            $"<!-- What happened, or what would you like to see? -->\n\n" +
            $"**Steps to reproduce (for bugs):**\n" +
            $"1. \n" +
            $"2. \n\n" +
            $"**Expected behaviour:**\n\n" +
            $"---\n\n" +
            $"**Environment (auto-detected):**\n" +
            $"{GatherEnvironmentInfo()}";

        return $"{IssueBaseUrl}?title={WebUtility.UrlEncode(title)}&body={WebUtility.UrlEncode(body)}";
    }

    /// <summary>
    /// Collects app version, OS, runtime, CPU count, architecture, and total RAM
    /// as a markdown bullet list. No P/Invoke or WMI required.
    /// </summary>
    public static string GatherEnvironmentInfo()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        var appVersion = v is null ? "ClipStudio" : $"ClipStudio v{v.Major}.{v.Minor}.{v.Build}";
        var ramGb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024.0 / 1024.0;

        return
            $"- App: {appVersion}\n" +
            $"- OS: {RuntimeInformation.OSDescription}\n" +
            $"- Runtime: {RuntimeInformation.FrameworkDescription}\n" +
            $"- CPU: {Environment.ProcessorCount} cores ({RuntimeInformation.OSArchitecture})\n" +
            $"- RAM: {ramGb:F0} GB";
    }

    /// <summary>
    /// Extracts the simple exception type name from a crash dump string.
    /// Returns "Unknown exception" if the format is not recognised.
    /// </summary>
    private static string ExtractExceptionType(string crashText)
    {
        const string header = "=== Exception ===";
        var idx = crashText.IndexOf(header, StringComparison.Ordinal);
        if (idx < 0) return "Unknown exception";

        var afterHeader = crashText[(idx + header.Length)..].TrimStart('\r', '\n');
        var firstLine = afterHeader.Split('\n', 2, StringSplitOptions.RemoveEmptyEntries)[0];

        // Exception lines look like "System.InvalidOperationException: message"
        var colonIdx = firstLine.IndexOf(':');
        var typePart = colonIdx > 0 ? firstLine[..colonIdx].Trim() : firstLine.Trim();
        var lastDot = typePart.LastIndexOf('.');
        return lastDot >= 0 ? typePart[(lastDot + 1)..] : typePart;
    }
}
