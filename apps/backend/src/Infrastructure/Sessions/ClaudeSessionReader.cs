using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SessionInsights.Application.Common;
using SessionInsights.Domain.Reports;
using SessionInsights.Domain.Sessions;

namespace SessionInsights.Infrastructure.Sessions;

internal sealed class ClaudeSessionReader(
    IFileSystem fs,
    IOptions<SessionInsightsOptions> options,
    ILogger<ClaudeSessionReader> logger) : ISessionSourceReader
{
    private static readonly string[] IgnoredLinePrefixes =
    [
        "\"type\":\"api-request", "\"type\":\"file-history", "\"type\":\"last-prompt\"", "\"type\":\"queue-operation\"",
        "\"type\":\"mode\"", "\"type\":\"permission-mode\"", "\"type\":\"agent-setting\"", "\"type\":\"atis-latch\"",
    ];

    private static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(5);

    public SessionSource Source => SessionSource.Claude;

    public IReadOnlyList<SessionFileGroup> Discover()
    {
        var root = options.Value.ResolveClaudeRoot();
        if (!fs.Directory.Exists(root))
        {
            return [];
        }



        var byId = new Dictionary<string, SessionFileGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var projectDir in fs.Directory.EnumerateDirectories(root))
        {
            foreach (var file in fs.Directory.EnumerateFiles(projectDir, "*.jsonl"))
            {
                var id = fs.Path.GetFileNameWithoutExtension(file);
                if (!SessionIdFormat.IsValid(id))
                {
                    continue;
                }

                var files = new List<string> { file };
                var subagents = fs.Path.Combine(projectDir, id, "subagents");
                if (fs.Directory.Exists(subagents))
                {
                    files.AddRange(fs.Directory.EnumerateFiles(subagents, "agent-*.jsonl").OrderBy(f => f, StringComparer.Ordinal));
                }

                var group = new SessionFileGroup(id, SessionFileGroup.SignatureOf(fs, files), files);
                if (!byId.TryGetValue(id, out var existing) || fs.FileInfo.New(existing.Files[0]).Length < fs.FileInfo.New(file).Length)
                {
                    byId[id] = group;
                }
            }
        }

        return [.. byId.Values];
    }

    public Session? Parse(SessionFileGroup group, DateTimeOffset now)
    {
        try
        {
            return ParseCore(group, now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read Claude session {SessionId}", group.SessionId);
            return null;
        }
    }

    private Session? ParseCore(SessionFileGroup group, DateTimeOffset now)
    {
        var acc = new SessionAccumulator();
        var messages = new Dictionary<string, (string Model, TokenUsage Tokens, DateTimeOffset? At, bool Main)>(StringComparer.Ordinal);
        var toolUses = new Dictionary<string, (string Name, DateTimeOffset? At)>(StringComparer.Ordinal);
        var seenResults = new HashSet<string>(StringComparer.Ordinal);

        string? cwd = null, aiTitle = null, customTitle = null, recap = null, firstHuman = null, firstTitleSource = null, lastHuman = null, lastHumanEntrypoint = null;
        DateTimeOffset? recapAt = null;
        long apiDuration = 0;
        var humanTurns = 0;

        for (var i = 0; i < group.Files.Count; i++)
        {
            var isMainFile = i == 0;
            using var stream = fs.File.OpenRead(group.Files[i]);
            foreach (var doc in JsonLines.Read(stream, WantsLine))
            {
                using (doc)
                {
                    JsonElement? root = doc.RootElement;
                    var at = root.Time("timestamp");
                    acc.Seen(at);

                    switch (root.Str("type"))
                    {
                        case "user":
                            if (isMainFile)
                            {
                                cwd ??= root.Str("cwd");
                            }

                            HandleUser(root, at, isMainFile, acc, toolUses, seenResults, ref firstHuman, ref firstTitleSource, ref lastHuman, ref lastHumanEntrypoint, ref humanTurns);
                            break;

                        case "assistant":
                            if (isMainFile)
                            {
                                cwd ??= root.Str("cwd");
                            }

                            HandleAssistant(root, at, isMainFile, acc, messages, toolUses);
                            break;

                        case "attachment":
                            var attachment = root.Get("attachment");
                            if (isMainFile && HarnessEvidence.Claude(attachment, at, root.Str("version")) is { } observation)
                            {
                                acc.HarnessObservations.Add(observation);
                            }

                            HandleAttachment(attachment, at, acc, isMainFile);
                            break;

                        case "system" when root.Str("subtype") == "stop_hook_summary":
                            if (root.Get("hookErrors") is { ValueKind: JsonValueKind.Array } errors)
                            {
                                foreach (var _ in errors.EnumerateArray())
                                {
                                    acc.Guard(GuardKind.HookFailure, "Stop", at);
                                }
                            }

                            if (root.Bool("preventedContinuation") == true)
                            {
                                acc.Guard(GuardKind.BlockedHook, "Stop", at);
                            }

                            break;

                        case "ai-title" when isMainFile:
                            aiTitle = root.Str("aiTitle") ?? aiTitle;
                            break;

                        case "custom-title" when isMainFile:
                            customTitle = root.Str("customTitle") ?? customTitle;
                            break;

                        case "system" when root.Str("subtype") == "away_summary" && isMainFile:
                            if (!string.IsNullOrWhiteSpace(root.Str("content")))
                            {
                                recap = root.Str("content");
                                recapAt = at;
                            }

                            break;

                        case "cost-state" when isMainFile:
                            apiDuration = root.Num("totalAPIDuration");
                            break;
                    }
                }
            }
        }

        // Claude Code leaves title-only stub files (no turns) next to resumed sessions; they are not real sessions.
        if (humanTurns == 0 && messages.Count == 0 && acc.First is null)
        {
            return null;
        }

        var subagentFiles = group.Files.Count - 1;
        for (var extra = acc.SubagentCount; extra < subagentFiles; extra++)
        {
            acc.Subagent("unknown");
        }

        var byModel = new ModelAggregate();
        foreach (var message in messages.Values)
        {
            byModel.Add(message.Model, 1, message.Tokens);
            acc.TokenEvent(message.At, message.Tokens.Total);
        }

        var total = byModel.Entries.Aggregate(TokenUsage.Empty, (a, b) => a.Plus(b.Tokens));
        var started = acc.First ?? now;
        var lastWrite = group.Files.Select(f => fs.File.GetLastWriteTimeUtc(f)).Max();
        var isActive = now - new DateTimeOffset(lastWrite, TimeSpan.Zero) < ActiveWindow;
        DateTimeOffset? ended = isActive ? null : acc.Last;
        var effectiveEnd = ended ?? now;

        var isFork = string.Equals(firstHuman, ReportConstants.Prompt, StringComparison.OrdinalIgnoreCase)
            || (string.Equals(lastHuman, ReportConstants.Prompt, StringComparison.OrdinalIgnoreCase)
                && lastHumanEntrypoint?.StartsWith("sdk", StringComparison.OrdinalIgnoreCase) == true);

        return new Session
        {
            Id = group.SessionId,
            Source = SessionSource.Claude,
            Title = Session.TruncateTitle(FirstNonBlank(customTitle, aiTitle, firstTitleSource)),
            Recap = Session.TruncateRecap(recap),
            RecapAt = recap is null ? null : recapAt,
            Project = CopilotSessionReader.LeafOf(cwd),
            Cwd = cwd,
            StartedAt = started,
            LastActivityAt = acc.Last is { } lastSeen && lastSeen > started ? lastSeen : started,
            EndedAt = ended,
            DurationSeconds = Math.Max(0, (long)(effectiveEnd - started).TotalSeconds),
            Models = [.. byModel.Entries.Select(e => e.Model).OrderBy(m => m, StringComparer.Ordinal)],
            Tokens = total,
            TokensByModel = [.. byModel.Entries.OrderByDescending(m => m.Tokens.Total).Select(m => new ModelUsage(m.Model, m.Requests, m.Tokens))],
            ToolCallsSucceeded = acc.ToolsSucceeded,
            ToolCallsFailed = acc.ToolsFailed,
            Tools = acc.ToolStats(),
            HookTotal = acc.HookTotal,
            HookFailed = acc.HookFailed,
            HookTypes = acc.HookStats(),
            Guards = acc.GuardItems(),
            SubagentTotal = acc.SubagentCount,
            SubagentTypes = acc.SubagentCounts(),
            Skills = acc.SkillCounts(),
            UserTurns = humanTurns,
            AssistantTurns = messages.Values.Count(m => m.Main),
            ApiRequestCount = messages.Count,
            ApiDurationMs = apiDuration,
            Cost = null,
            CodeChanges = null,
            Timeline = acc.BuildTimeline(started, effectiveEnd < started ? started : effectiveEnd),
            Harness = acc.BuildHarness(),
            Notes =
            [
                "Claude Code does not expose premium requests, AIU cost or code-change totals; those are not available for this CLI.",
                "Subagent transcripts (subagents/agent-*.jsonl) are aggregated into this session.",
                "Permission denials are user lines carrying a non-accept permission decision or a tool denial kind.",
                "Hook blocks come from blocking-error hook attachments and Stop hooks that prevented continuation.",
            ],
            LooksLikeReportFork = isFork,
        };
    }

    private static void HandleUser(
        JsonElement? root,
        DateTimeOffset? at,
        bool isMainFile,
        SessionAccumulator acc,
        Dictionary<string, (string Name, DateTimeOffset? At)> toolUses,
        HashSet<string> seenResults,
        ref string? firstHuman,
        ref string? firstTitleSource,
        ref string? lastHuman,
        ref string? lastHumanEntrypoint,
        ref int humanTurns)
    {
        var message = root.Get("message");
        var content = message.Get("content");
        string? toolNameForDenial = null;
        var hasToolResult = false;

        if (content is { ValueKind: JsonValueKind.Array } blocks)
        {
            foreach (var block in blocks.EnumerateArray())
            {
                JsonElement? b = block;
                if (b.Str("type") != "tool_result" || b.Str("tool_use_id") is not { } id)
                {
                    continue;
                }

                hasToolResult = true;
                if (!seenResults.Add(id))
                {
                    continue;
                }

                toolUses.TryGetValue(id, out var use);
                var name = use.Name ?? "unknown";
                toolNameForDenial ??= name;
                var duration = use.At is { } s && at is { } e ? (long)(e - s).TotalMilliseconds : 0;
                acc.Tool(name, b.Bool("is_error") != true, duration, at, isMainFile, use.At);
            }
        }

        var decision = root.Get("permissionDecision").Str("decision");
        if ((decision is not null && !decision.Equals("accept", StringComparison.OrdinalIgnoreCase)) || root.Get("toolDenialKind") is not null)
        {
            acc.Guard(GuardKind.PermissionDenial, toolNameForDenial ?? "unknown", at);
        }

        if (!isMainFile || hasToolResult || root.Bool("isMeta") == true || root.Get("sourceToolUseID") is not null)
        {
            return;
        }

        var origin = root.Get("origin").Str("kind");
        if (origin is not null && origin != "human")
        {
            return;
        }

        var text = content switch
        {
            { ValueKind: JsonValueKind.String } s => s.GetString(),
            { ValueKind: JsonValueKind.Array } a => a.EnumerateArray().Select(x => (JsonElement?)x).FirstOrDefault(x => x.Str("type") == "text").Str("text"),
            _ => null,
        };
        if (text is null)
        {
            return;
        }

        text = text.Trim();
        firstHuman ??= text;
        firstTitleSource ??= Session.CleanTitle(text) is null ? null : text;
        lastHuman = text;
        lastHumanEntrypoint = root.Str("entrypoint");
        if (!string.Equals(text, ReportConstants.Prompt, StringComparison.OrdinalIgnoreCase))
        {
            humanTurns++;
        }
    }

    private static void HandleAssistant(
        JsonElement? root,
        DateTimeOffset? at,
        bool isMainFile,
        SessionAccumulator acc,
        Dictionary<string, (string Model, TokenUsage Tokens, DateTimeOffset? At, bool Main)> messages,
        Dictionary<string, (string Name, DateTimeOffset? At)> toolUses)
    {
        var message = root.Get("message");
        var model = message.Str("model");
        var id = message.Str("id");
        var usage = message.Get("usage");

        if (id is not null && model is not null && model != "<synthetic>" && root.Bool("isApiErrorMessage") != true && usage is not null)
        {
            var tokens = new TokenUsage(
                usage.Num("input_tokens"),
                usage.Num("output_tokens"),
                usage.Num("cache_read_input_tokens"),
                usage.Num("cache_creation_input_tokens"),
                usage.Get("output_tokens_details").Num("thinking_tokens"));
            if (!messages.TryGetValue(id, out var existing) || existing.Tokens.Total <= tokens.Total)
            {
                messages[id] = (model, tokens, at, isMainFile);
            }
        }

        if (message.Get("content") is not { ValueKind: JsonValueKind.Array } blocks)
        {
            return;
        }

        foreach (var block in blocks.EnumerateArray())
        {
            JsonElement? b = block;
            if (b.Str("type") != "tool_use" || b.Str("id") is not { } toolId || b.Str("name") is not { } name)
            {
                continue;
            }

            if (!toolUses.TryAdd(toolId, (name, at)))
            {
                continue;
            }

            if (name is "Agent" or "Task")
            {
                acc.Subagent(b.Get("input").Str("subagent_type") ?? "unknown");
            }
            else if (name == "Skill")
            {
                acc.Skill(b.Get("input").Str("skill") ?? "unknown");
            }
        }
    }

    private static void HandleAttachment(JsonElement? attachment, DateTimeOffset? at, SessionAccumulator acc, bool isMainFile)
    {
        var type = attachment.Str("type");
        if (type is null || !type.StartsWith("hook_", StringComparison.Ordinal))
        {
            return;
        }

        var hookEvent = attachment.Str("hookEvent") ?? "unknown";
        var name = attachment.Str("hookName") ?? hookEvent;
        var durationValue = attachment.Get("durationMs");
        long? duration = durationValue is { ValueKind: JsonValueKind.Number } value
            ? value.TryGetInt64(out var milliseconds) ? milliseconds : (long)value.GetDouble()
            : null;

        switch (type)
        {
            case "hook_success":
                var exit = attachment.Get("exitCode") is { } e ? (e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString()) : "0";
                var failed = exit is not null and not "0";
                acc.Hook(hookEvent, failed, duration, at, isMainFile);
                if (failed)
                {
                    acc.Guard(GuardKind.HookFailure, name, at);
                }

                break;
            case "hook_blocking_error" or "hook_stopped_continuation":
                acc.Hook(hookEvent, true, duration, at, isMainFile);
                acc.Guard(GuardKind.BlockedHook, name, at);
                break;
            case "hook_non_blocking_error" or "hook_error_during_execution" or "hook_cancelled":
                acc.Hook(hookEvent, true, duration, at, isMainFile);
                acc.Guard(GuardKind.HookFailure, name, at);
                break;
        }
    }

    private static string? FirstNonBlank(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static bool WantsLine(string line)
    {
        foreach (var prefix in IgnoredLinePrefixes)
        {
            if (line.Contains(prefix, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
