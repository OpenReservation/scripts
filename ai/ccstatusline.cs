#!/usr/bin/env dotnet
// ---------------------------------------------------------------------------
// Claude Code status line — file-based C# app (top-level statements),
// Native AOT friendly.
//
// Run directly (JIT, for iterating):
//   dotnet ccstatusline.cs
//
// Publish as a native AOT binary (recommended — starts in a few ms, no JIT/
// runtime startup cost, which matters because Claude Code re-runs this on
// every assistant message). File-based apps publish with PublishAot=true by
// default, so no extra properties are needed:
//   dotnet publish ccstatusline.cs -o ~/.claude/
//   # -> ~/.claude/ccstatusline (or statusline.exe on Windows)
//
// Then point Claude Code at the compiled binary in settings.json:
//   {
//     "statusLine": { "type": "command", "command": "~/.claude/statusline" }
//   }
//
// Docs: https://code.claude.com/docs/en/statusline
// ---------------------------------------------------------------------------

#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property Nullable=enable
#:property StripSymbols=true

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// ============================================================================
// Entry point (top-level statements)
// ============================================================================

try
{
    string raw = Console.In.ReadToEnd();
    var data = JsonSerializer.Deserialize(raw, AppJsonContext.Default.StatusLineInput);
    if (data is null)
    {
        Console.WriteLine("[statusline: no input]");
        return;
    }

    try { Console.OutputEncoding = Encoding.UTF8; } catch { /* stdout may be redirected/non-console; ignore */ }

    Console.WriteLine(BuildLine1(data));

    string? line2 = BuildLine2(data);
    if (line2 is not null) Console.WriteLine(line2);

    string? line3 = BuildLine3(data);
    if (line3 is not null) Console.WriteLine(line3);
}
catch (Exception ex)
{
    // Never let a bad/partial JSON payload leave the status line blank
    // without explanation — print something short and exit 0 so
    // Claude Code doesn't just show nothing.
    Console.WriteLine($"[statusline error: {ex.Message}]");
}

// ============================================================================
// Local functions
// ============================================================================

// Line 1: model, effort, output style, working directory
static string BuildLine1(StatusLineInput data)
{
    var sb = new StringBuilder();

    string model = data.Model?.DisplayName ?? data.Model?.Id ?? "unknown-model";
    sb.Append(Color.Cyan).Append('[').Append(model).Append(']').Append(Color.Reset);

    if (data.Effort?.Level is { Length: > 0 } effort)
    {
        sb.Append(" ⚙ ").Append(Color.Magenta).Append(effort).Append(Color.Reset);
    }

    if (data.OutputStyle?.Name is { Length: > 0 } style && !string.Equals(style, "default", StringComparison.OrdinalIgnoreCase))
    {
        sb.Append(" 🎨 ").Append(style);
    }

    string dir = data.Workspace?.CurrentDir ?? data.Cwd ?? "";
    string dirName = dir.Length == 0 ? "?" : Path.GetFileName(dir.TrimEnd('/', '\\'));
    if (dirName.Length == 0) dirName = dir; // e.g. root "/"
    sb.Append(" 📁 ").Append(dirName);

    return sb.ToString();
}

// Line 2: git branch/status (if in a repo), PR info
static string? BuildLine2(StatusLineInput data)
{
    string cwd = data.Workspace?.CurrentDir ?? data.Cwd ?? Environment.CurrentDirectory;
    string sessionId = data.SessionId ?? "nosession";

    GitStatus? git = GitStatusCache.GetOrCompute(cwd, sessionId);
    if (git is null || !git.IsRepo) return null;

    var sb = new StringBuilder();
    string branch = data.Worktree?.Branch ?? git.Branch ?? "(detached)";
    sb.Append("🌿 ").Append(Color.Green).Append(branch).Append(Color.Reset);

    if (git.Staged > 0) sb.Append(' ').Append(Color.Green).Append('+').Append(git.Staged).Append(Color.Reset);
    if (git.Modified > 0) sb.Append(' ').Append(Color.Yellow).Append('~').Append(git.Modified).Append(Color.Reset);
    if (git.Untracked > 0) sb.Append(' ').Append(Color.Red).Append('?').Append(git.Untracked).Append(Color.Reset);

    if (data.Worktree?.Name is { Length: > 0 } wt) sb.Append(" 🌳 ").Append(wt);

    if (data.Pr?.Number is { } prNumber)
    {
        string state = data.Pr.ReviewState switch
        {
            "approved" => "✅",
            "changes_requested" => "❌",
            "draft" => "📝",
            _ => "⏳",
        };
        sb.Append(" ").Append(state).Append(" PR#").Append(prNumber);
    }

    return sb.ToString();
}

// Line 3: context usage bar, cost, duration, lines changed, rate limits
static string? BuildLine3(StatusLineInput data)
{
    var sb = new StringBuilder();
    bool any = false;

    double? pctNullable = data.ContextWindow?.UsedPercentage;
    if (pctNullable is { } pctRaw)
    {
        int pct = (int)Math.Round(pctRaw);
        string barColor = pct >= 90 ? Color.Red : pct >= 70 ? Color.Yellow : Color.Green;
        sb.Append(barColor).Append(Bar(pct, 10)).Append(Color.Reset).Append(' ').Append(pct).Append("% ctx");
        any = true;
    }

    if (data.Cost?.TotalCostUsd is { } cost)
    {
        if (any) sb.Append(" | ");
        sb.Append(Color.Yellow).Append('$').Append(cost.ToString("0.00")).Append(Color.Reset);
        any = true;
    }

    if (data.Cost?.TotalDurationMs is { } durationMs)
    {
        if (any) sb.Append(" | ");
        sb.Append("⏱ ").Append(FormatDuration(durationMs));
        any = true;
    }

    long added = data.Cost?.TotalLinesAdded ?? 0;
    long removed = data.Cost?.TotalLinesRemoved ?? 0;
    if (added > 0 || removed > 0)
    {
        if (any) sb.Append(" | ");
        sb.Append(Color.Green).Append('+').Append(added).Append(Color.Reset)
          .Append('/')
          .Append(Color.Red).Append('-').Append(removed).Append(Color.Reset);
        any = true;
    }

    double? fiveHour = data.RateLimits?.FiveHour?.UsedPercentage;
    double? sevenDay = data.RateLimits?.SevenDay?.UsedPercentage;
    if (fiveHour is not null || sevenDay is not null)
    {
        if (any) sb.Append(" | ");
        var parts = new StringBuilder();
        if (fiveHour is { } fh) parts.Append("5h:").Append(Math.Round(fh)).Append('%');
        if (sevenDay is { } sd)
        {
            if (parts.Length > 0) parts.Append(' ');
            parts.Append("7d:").Append(Math.Round(sd)).Append('%');
        }
        sb.Append(parts);
        any = true;
    }

    return any ? sb.ToString() : null;
}

static string Bar(int pct, int width)
{
    pct = Math.Clamp(pct, 0, 100);
    int filled = pct * width / 100;
    return new string('█', filled) + new string('░', width - filled);
}

static string FormatDuration(long ms)
{
    long totalSeconds = ms / 1000;
    long h = totalSeconds / 3600;
    long m = (totalSeconds % 3600) / 60;
    long s = totalSeconds % 60;
    return h > 0 ? $"{h}h{m:D2}m" : m > 0 ? $"{m}m{s:D2}s" : $"{s}s";
}

// ============================================================================
// Type declarations (must follow top-level statements in a file that uses
// them, per C# rules)
// ============================================================================

// ---- JSON model — mirrors the stdin schema documented at
// https://code.claude.com/docs/en/statusline. Only fields we actually use
// are modeled; everything is nullable since most fields can be absent.
// A source-generated JsonSerializerContext is used (not reflection-based
// serialization) so this stays fully compatible with Native AOT + trimming.

internal sealed class StatusLineInput
{
    [JsonPropertyName("cwd")] public string? Cwd { get; set; }
    [JsonPropertyName("session_id")] public string? SessionId { get; set; }
    [JsonPropertyName("session_name")] public string? SessionName { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("model")] public ModelInfo? Model { get; set; }
    [JsonPropertyName("workspace")] public WorkspaceInfo? Workspace { get; set; }
    [JsonPropertyName("cost")] public CostInfo? Cost { get; set; }
    [JsonPropertyName("context_window")] public ContextWindowInfo? ContextWindow { get; set; }
    [JsonPropertyName("effort")] public EffortInfo? Effort { get; set; }
    [JsonPropertyName("thinking")] public ThinkingInfo? Thinking { get; set; }
    [JsonPropertyName("output_style")] public OutputStyleInfo? OutputStyle { get; set; }
    [JsonPropertyName("rate_limits")] public RateLimitsInfo? RateLimits { get; set; }
    [JsonPropertyName("pr")] public PrInfo? Pr { get; set; }
    [JsonPropertyName("worktree")] public WorktreeInfo? Worktree { get; set; }
}

internal sealed class ModelInfo
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
}

internal sealed class WorkspaceInfo
{
    [JsonPropertyName("current_dir")] public string? CurrentDir { get; set; }
    [JsonPropertyName("project_dir")] public string? ProjectDir { get; set; }
    [JsonPropertyName("git_worktree")] public string? GitWorktree { get; set; }
    [JsonPropertyName("repo")] public RepoInfo? Repo { get; set; }
}

internal sealed class RepoInfo
{
    [JsonPropertyName("host")] public string? Host { get; set; }
    [JsonPropertyName("owner")] public string? Owner { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

internal sealed class CostInfo
{
    [JsonPropertyName("total_cost_usd")] public double? TotalCostUsd { get; set; }
    [JsonPropertyName("total_duration_ms")] public long? TotalDurationMs { get; set; }
    [JsonPropertyName("total_api_duration_ms")] public long? TotalApiDurationMs { get; set; }
    [JsonPropertyName("total_lines_added")] public long? TotalLinesAdded { get; set; }
    [JsonPropertyName("total_lines_removed")] public long? TotalLinesRemoved { get; set; }
}

internal sealed class ContextWindowInfo
{
    [JsonPropertyName("context_window_size")] public long? ContextWindowSize { get; set; }
    [JsonPropertyName("used_percentage")] public double? UsedPercentage { get; set; }
    [JsonPropertyName("remaining_percentage")] public double? RemainingPercentage { get; set; }
}

internal sealed class EffortInfo
{
    [JsonPropertyName("level")] public string? Level { get; set; }
}

internal sealed class ThinkingInfo
{
    [JsonPropertyName("enabled")] public bool? Enabled { get; set; }
}

internal sealed class OutputStyleInfo
{
    [JsonPropertyName("name")] public string? Name { get; set; }
}

internal sealed class RateLimitsInfo
{
    [JsonPropertyName("five_hour")] public RateLimitWindow? FiveHour { get; set; }
    [JsonPropertyName("seven_day")] public RateLimitWindow? SevenDay { get; set; }
}

internal sealed class RateLimitWindow
{
    [JsonPropertyName("used_percentage")] public double? UsedPercentage { get; set; }
}

internal sealed class PrInfo
{
    [JsonPropertyName("number")] public long? Number { get; set; }
    [JsonPropertyName("review_state")] public string? ReviewState { get; set; }
}

internal sealed class WorktreeInfo
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("branch")] public string? Branch { get; set; }
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(StatusLineInput))]
internal partial class AppJsonContext : JsonSerializerContext;

// ---- ANSI colors — plain constants, no allocation-heavy formatting needed.
internal static class Color
{
    public const string Reset = "\e[0m";
    public const string Cyan = "\e[36m";
    public const string Green = "\e[32m";
    public const string Yellow = "\e[33m";
    public const string Red = "\e[31m";
    public const string Magenta = "\e[35m";
}

// ---- Git status, computed by shelling out to `git` (no libgit2/managed git
// dependency needed — keeps this AOT-trim-friendly with zero extra
// packages) and cached to a temp file keyed by session id, exactly like the
// caching pattern in the official docs, so repeated status line refreshes
// during an active session don't repeatedly pay for `git status`.
internal sealed record GitStatus(bool IsRepo, string? Branch, int Staged, int Modified, int Untracked);

internal static class GitStatusCache
{
    private const int CacheMaxAgeSeconds = 5;

    public static GitStatus? GetOrCompute(string cwd, string sessionId)
    {
        if (string.IsNullOrEmpty(cwd)) return null;

        string cacheFile = Path.Combine(Path.GetTempPath(), $"statusline-git-cache-{Sanitize(sessionId)}");

        if (File.Exists(cacheFile))
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile);
            if (age.TotalSeconds <= CacheMaxAgeSeconds)
            {
                string cached = File.ReadAllText(cacheFile);
                return Parse(cached);
            }
        }

        GitStatus status = Compute(cwd);
        try
        {
            File.WriteAllText(cacheFile, Serialize(status));
        }
        catch
        {
            // Cache write failures (e.g. read-only tmp) shouldn't break the status line.
        }

        return status;
    }

    private static GitStatus Compute(string cwd)
    {
        if (!RunGit(cwd, "status --porcelain=v1 -b", out string output))
            return new GitStatus(false, null, 0, 0, 0);

        string? branch = null;
        int staged = 0, modified = 0, untracked = 0;

        foreach (string line in output.Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                string b = line.Substring(3);
                int dot = b.IndexOf("...", StringComparison.Ordinal);
                b = dot >= 0 ? b.Substring(0, dot) : b;
                branch = b == "HEAD (no branch)" || string.IsNullOrEmpty(b) ? null : b;
            }
            else if (line.Length >= 2)
            {
                char x = line[0], y = line[1];
                if (x == '?' && y == '?') untracked++;
                else
                {
                    if (x != ' ') staged++;
                    if (y != ' ') modified++;
                }
            }
        }

        return new GitStatus(true, branch, staged, modified, untracked);
    }

    private static bool RunGit(string cwd, string args, out string stdout)
    {
        stdout = "";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return false;

            stdout = proc.StandardOutput.ReadToEnd();
            bool exited = proc.WaitForExit(2000);
            return exited && proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        }
        return sb.Length == 0 ? "unknown" : sb.ToString();
    }

    // Tiny fixed-shape "repo\nbranch\nstaged\nmodified\nuntracked" cache format —
    // avoids pulling JSON (de)serialization into this hot path for a 5-field record.
    // Uses \n as delimiter because git branch names cannot contain newlines.
    private static string Serialize(GitStatus s) =>
        $"{(s.IsRepo ? 1 : 0)}\n{s.Branch}\n{s.Staged}\n{s.Modified}\n{s.Untracked}";

    private static GitStatus Parse(string s)
    {
        string[] parts = s.Split('\n');
        if (parts.Length != 5) return new GitStatus(false, null, 0, 0, 0);

        bool isRepo = parts[0] == "1";
        string? branch = string.IsNullOrEmpty(parts[1]) ? null : parts[1];
        int.TryParse(parts[2], out int staged);
        int.TryParse(parts[3], out int modified);
        int.TryParse(parts[4], out int untracked);
        return new GitStatus(isRepo, branch, staged, modified, untracked);
    }
}
