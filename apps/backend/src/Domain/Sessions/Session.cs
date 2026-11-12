using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SessionInsights.Domain.Sessions;

/// <summary>A parsed session. Holds metrics only: never prompt, response, tool arguments or tool output.</summary>
public sealed class Session
{
    public const int MaxTitleLength = 120;

    public required string Id { get; init; }
    public required SessionSource Source { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Project { get; init; }

    /// <summary>Latest AI-written recap of the session (Claude /recap, Copilot checkpoint overview), when the CLI stored one.</summary>
    public string? Recap { get; init; }
    public DateTimeOffset? RecapAt { get; init; }

    /// <summary>Working directory of the session. Internal: used to run reports, never returned by the API.</summary>
    public string? Cwd { get; init; }

    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset LastActivityAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public long DurationSeconds { get; init; }

    public IReadOnlyList<string> Models { get; init; } = [];
    public TokenUsage Tokens { get; init; } = TokenUsage.Empty;
    public IReadOnlyList<ModelUsage> TokensByModel { get; init; } = [];

    public int ToolCallsSucceeded { get; init; }
    public int ToolCallsFailed { get; init; }
    public int ToolCallTotal => ToolCallsSucceeded + ToolCallsFailed;
    public IReadOnlyList<ToolStat> Tools { get; init; } = [];

    public int HookTotal { get; init; }
    public int HookFailed { get; init; }
    public IReadOnlyList<HookStat> HookTypes { get; init; } = [];

    public IReadOnlyList<GuardItem> Guards { get; init; } = [];
    public int GuardTotal => Guards.Sum(g => g.Count);

    public int SubagentTotal { get; init; }
    public IReadOnlyList<NamedCount> SubagentTypes { get; init; } = [];
    public IReadOnlyList<NamedCount> Skills { get; init; } = [];

    public int UserTurns { get; init; }
    public int AssistantTurns { get; init; }

    public long ApiRequestCount { get; init; }
    public long ApiDurationMs { get; init; }

    public CostInfo? Cost { get; init; }
    public CodeChangeInfo? CodeChanges { get; init; }

    public IReadOnlyList<TimelineBucket> Timeline { get; init; } = [];
    public IReadOnlyList<string> Notes { get; init; } = [];

    public HarnessInfo Harness { get; init; } = HarnessInfo.Empty;

    /// <summary>True when the session is a report-run fork (first/last human message is the report prompt).</summary>
    public bool LooksLikeReportFork { get; init; }

    public const string UntitledTitle = "Untitled session";

    private static readonly Regex CommandName = new(@"<command-name>\s*(.*?)\s*</command-name>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex CommandArgs = new(@"<command-args>\s*(.*?)\s*</command-args>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex SystemBlocks = new(@"<(local-command-stdout|local-command-stderr|local-command-caveat|system-reminder)>.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex AnyTag = new(@"</?[A-Za-z][^<>]*>", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Returns a readable title from raw text, or null when nothing meaningful is left (pure system markup).</summary>
    public static string? CleanTitle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string text;
        var command = CommandName.Match(raw);
        if (command.Success && command.Groups[1].Value.Trim() is { Length: > 0 } name)
        {
            var args = CommandArgs.Match(raw);
            text = args.Success && args.Groups[1].Value.Trim() is { Length: > 0 } a ? $"{name} {a}" : name;
        }
        else
        {
            text = AnyTag.Replace(SystemBlocks.Replace(raw, " "), " ");
        }

        text = Spaces.Replace(AnyTag.Replace(text, " "), " ").Trim();
        return text.Length == 0 ? null : text;
    }

    public const int MaxRecapLength = 2000;

    public static string? TruncateRecap(string? recap)
    {
        var clean = recap?.Trim();
        if (string.IsNullOrEmpty(clean))
        {
            return null;
        }

        return clean.Length <= MaxRecapLength ? clean : clean[..MaxRecapLength];
    }

    public static string TruncateTitle(string? title)
    {
        var clean = CleanTitle(title);
        if (clean is null)
        {
            return UntitledTitle;
        }

        return clean.Length <= MaxTitleLength ? clean : clean[..MaxTitleLength];
    }
}
