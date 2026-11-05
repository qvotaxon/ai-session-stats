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
using YamlDotNet.Serialization;

namespace SessionInsights.Infrastructure.Sessions;

internal sealed class CopilotSessionReader(
    IFileSystem fs,
    IOptions<SessionInsightsOptions> options,
    ILogger<CopilotSessionReader> logger) : ISessionSourceReader
{
    private static readonly HashSet<string> WantedTypes =
    [
        "session.start", "session.shutdown", "user.message", "assistant.turn_start", "tool.execution_start", "tool.execution_complete",
        "hook.start", "hook.end", "subagent.started", "skill.invoked", "skill.invoked_ref", "session.usage_record", "session.usage_checkpoint", "session.compaction_complete",
    ];

    private static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(5);

    public SessionSource Source => SessionSource.Copilot;

    public IReadOnlyList<SessionFileGroup> Discover()
    {
        var root = options.Value.ResolveCopilotRoot();
        if (!fs.Directory.Exists(root))
        {
            return [];
        }

        var groups = new List<SessionFileGroup>();
        foreach (var dir in fs.Directory.EnumerateDirectories(root))
        {
            var id = fs.Path.GetFileName(dir);
            var events = fs.Path.Combine(dir, "events.jsonl");
            if (!SessionIdFormat.IsValid(id) || !fs.File.Exists(events))
            {
                continue;
            }

            var files = new List<string> { events };
            var workspace = fs.Path.Combine(dir, "workspace.yaml");
            if (fs.File.Exists(workspace))
            {
                files.Add(workspace);
            }

            groups.Add(new SessionFileGroup(id, SessionFileGroup.SignatureOf(fs, files), files));
        }

        return groups;
    }

    public Session? Parse(SessionFileGroup group, DateTimeOffset now)
    {
        try
        {
            return ParseCore(group, now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read Copilot session {SessionId}", group.SessionId);
            return null;
        }
    }

    private Session ParseCore(SessionFileGroup group, DateTimeOffset now)
    {
        var eventsPath = group.Files[0];
        var workspace = ReadWorkspace(group.Files.Skip(1).FirstOrDefault());
        var acc = new SessionAccumulator();

        var shutdown = new ModelAggregate();
        var pending = new ModelAggregate();
        var windowPending = new ModelAggregate();
        var windowBeforeShutdown = new ModelAggregate();
        var models = new HashSet<string>(StringComparer.Ordinal);
        var toolStarts = new Dictionary<string, (string Name, DateTimeOffset? At, bool InWindow)>(StringComparer.Ordinal);
        var hookStarts = new Dictionary<string, (DateTimeOffset? At, bool InWindow)>(StringComparer.Ordinal);

        var inWindow = false;
        var hasReportTurn = false;
        var shutdownCount = 0;
        double premium = 0, nanoAiu = 0, pendingPremium = 0, checkpointNano = 0;
        long apiDuration = 0, pendingApiDuration = 0, requests = 0, pendingRequests = 0;
        long added = 0, removed = 0, files = 0;
        string? firstUserMessage = null;
        string? firstHumanMessage = null;
        string? recap = null;
        DateTimeOffset? recapAt = null;
        DateTimeOffset? sessionStart = null;

        using var stream = fs.File.OpenRead(eventsPath);
        foreach (var doc in JsonLines.Read(stream, WantsLine))
        {
            using (doc)
            {
                JsonElement? root = doc.RootElement;
                var type = root.Str("type");
                var data = root.Get("data");
                var at = root.Time("timestamp");
                acc.Seen(at);

                switch (type)
                {
                    case "session.start":
                        sessionStart = data.Time("startTime") ?? at;
                        break;

                    case "user.message":
                        var content = data.Str("content")?.Trim();
                        firstHumanMessage ??= content;
                        if (string.Equals(content, ReportConstants.Prompt, StringComparison.OrdinalIgnoreCase))
                        {
                            inWindow = true;
                            hasReportTurn = true;
                        }
                        else
                        {
                            inWindow = false;
                            firstUserMessage ??= content;
                            acc.UserTurns++;
                        }

                        break;

                    case "session.compaction_complete":
                        if (OverviewOf(data.Str("summaryContent")) is { } overview)
                        {
                            recap = overview;
                            recapAt = at;
                        }

                        break;

                    case "assistant.turn_start" when !inWindow:
                        acc.AssistantTurns++;
                        break;

                    case "tool.execution_start" when data.Str("toolCallId") is { } callId:
                        toolStarts[callId] = (data.Str("toolName") ?? "unknown", at, inWindow);
                        break;

                    case "tool.execution_complete" when data.Str("toolCallId") is { } callId:
                        toolStarts.Remove(callId, out var start);
                        start = start.Name is null ? ("unknown", null, inWindow) : start;
                        if (start.InWindow)
                        {
                            break;
                        }

                        var denied = data.Get("error").Str("code") == "denied";
                        var succeeded = data.Bool("success") ?? true;
                        var toolDuration = start.At is { } s && at is { } e ? (long)(e - s).TotalMilliseconds : 0;
                        acc.Tool(start.Name, succeeded, toolDuration, at);
                        if (denied)
                        {
                            acc.Guard(GuardKind.PermissionDenial, start.Name, at);
                        }

                        break;

                    case "hook.start" when data.Str("hookInvocationId") is { } hookId:
                        hookStarts[hookId] = (at, inWindow);
                        break;

                    case "hook.end":
                        (DateTimeOffset? At, bool InWindow) hs = default;
                        if (data.Str("hookInvocationId") is { } endId)
                        {
                            hookStarts.Remove(endId, out hs);
                        }

                        if (hs.InWindow || (hs.At is null && inWindow))
                        {
                            break;
                        }

                        var hookType = data.Str("hookType") ?? "unknown";
                        var failed = data.Bool("success") == false;
                        var hookDuration = hs.At is { } hStart && at is { } hEnd ? (long)(hEnd - hStart).TotalMilliseconds : 0;
                        acc.Hook(hookType, failed, hookDuration, at);
                        if (failed)
                        {
                            acc.Guard(GuardKind.HookFailure, hookType, at);
                        }
                        else if (IsBlocking(data.Get("output")))
                        {
                            acc.Guard(GuardKind.BlockedHook, hookType, at);
                        }

                        break;

                    case "subagent.started" when !inWindow:
                        acc.Subagent(data.Str("agentName") ?? "unknown");
                        break;

                    case "skill.invoked" or "skill.invoked_ref" when !inWindow:
                        acc.Skill(data.Str("name") ?? "unknown");
                        break;

                    case "session.usage_record":
                        var usage = data.Get("usage");
                        var model = usage.Str("model") ?? "unknown";
                        var tokens = ToTokens(usage);
                        models.Add(model);
                        if (inWindow)
                        {
                            windowPending.Add(model, 1, tokens);
                        }
                        else
                        {
                            pending.Add(model, 1, tokens);
                            pendingPremium += usage.Dbl("cost");
                            pendingApiDuration += usage.Num("duration");
                            pendingRequests++;
                            acc.TokenEvent(at, tokens.Total);
                        }

                        break;

                    case "session.usage_checkpoint":
                        checkpointNano = data.Dbl("totalNanoAiu");
                        break;

                    case "session.shutdown":
                        shutdownCount++;
                        // Shutdown totals are CUMULATIVE across resumes (and sometimes repeated verbatim), so keep the maximum, never the sum.
                        premium = Math.Max(premium, data.Dbl("totalPremiumRequests"));
                        nanoAiu = Math.Max(nanoAiu, data.Dbl("totalNanoAiu"));
                        apiDuration = Math.Max(apiDuration, data.Num("totalApiDurationMs"));
                        if (data.Get("modelMetrics") is { ValueKind: JsonValueKind.Object } metrics)
                        {
                            foreach (var m in metrics.EnumerateObject())
                            {
                                JsonElement? metric = m.Value;
                                var count = metric.Get("requests").Num("count");
                                shutdown.Max(m.Name, count, ToTokens(metric.Get("usage")));
                                models.Add(m.Name);
                            }
                        }

                        var changes = data.Get("codeChanges");
                        added = Math.Max(added, changes.Num("linesAdded"));
                        removed = Math.Max(removed, changes.Num("linesRemoved"));
                        files = Math.Max(files, changes.Get("filesModified") is { ValueKind: JsonValueKind.Array } arr ? arr.GetArrayLength() : changes.Num("filesModified"));

                        windowBeforeShutdown.AddAll(windowPending);
                        windowPending = new ModelAggregate();
                        pending = new ModelAggregate();
                        pendingPremium = 0;
                        pendingApiDuration = 0;
                        pendingRequests = 0;
                        break;
                }
            }
        }

        var byModel = shutdown.Minus(windowBeforeShutdown).Merge(pending);
        var totalTokens = byModel.Entries.Aggregate(TokenUsage.Empty, (a, b) => a.Plus(b.Tokens));
        var started = acc.First is { } f && sessionStart is { } ss ? (f < ss ? f : ss) : acc.First ?? sessionStart ?? workspace.Created ?? now;
        var lastWrite = fs.File.GetLastWriteTimeUtc(eventsPath);
        var isActive = now - new DateTimeOffset(lastWrite, TimeSpan.Zero) < ActiveWindow;
        DateTimeOffset? ended = isActive ? null : acc.Last;
        var effectiveEnd = ended ?? now;
        var duration = Math.Max(0, (long)(effectiveEnd - started).TotalSeconds);

        var notes = new List<string>
        {
            "Hook blocks are only detected when a hook returns an explicit deny/block decision; Copilot does not log a separate hook-block event.",
            "Permission denials are tool calls that finished with error code 'denied'.",
        };
        if (hasReportTurn)
        {
            notes.Add("Session Insights report turns are excluded from tokens, tools, hooks and turns; cost and API duration come from shutdown totals and may include them.");
        }

        CostInfo cost = shutdownCount > 0
            ? new CostInfo(premium + pendingPremium, Math.Max(nanoAiu, checkpointNano))
            : new CostInfo(pendingPremium, checkpointNano);
        CodeChangeInfo? codeChanges = shutdownCount > 0 ? new CodeChangeInfo(added, removed, files) : null;
        if (codeChanges is null)
        {
            notes.Add("Code change totals are only written by Copilot when a session shuts down.");
        }

        var title = Session.TruncateTitle(FirstNonEmpty(workspace.Name, workspace.Summary, firstUserMessage));
        return new Session
        {
            Id = group.SessionId,
            Source = SessionSource.Copilot,
            Title = title,
            Recap = Session.TruncateRecap(recap),
            RecapAt = recap is null ? null : recapAt,
            Project = LeafOf(workspace.Cwd),
            Cwd = workspace.Cwd,
            StartedAt = started,
            LastActivityAt = acc.Last is { } lastSeen && lastSeen > started ? lastSeen : started,
            EndedAt = ended,
            DurationSeconds = duration,
            Models = [.. models.OrderBy(m => m, StringComparer.Ordinal)],
            Tokens = totalTokens,
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
            UserTurns = acc.UserTurns,
            AssistantTurns = acc.AssistantTurns,
            ApiRequestCount = shutdown.Entries.Sum(e => e.Requests) + pendingRequests,
            ApiDurationMs = shutdownCount > 0 ? apiDuration + pendingApiDuration : pendingApiDuration,
            Cost = cost,
            CodeChanges = codeChanges,
            Timeline = acc.BuildTimeline(started, effectiveEnd < started ? started : effectiveEnd),
            Notes = notes,
            LooksLikeReportFork = string.Equals(firstHumanMessage, ReportConstants.Prompt, StringComparison.OrdinalIgnoreCase),
        };
    }

    private static string? OverviewOf(string? summary)
    {
        const string open = "<overview>", close = "</overview>";
        if (summary is null)
        {
            return null;
        }

        var start = summary.IndexOf(open, StringComparison.Ordinal);
        var end = summary.IndexOf(close, StringComparison.Ordinal);
        if (start < 0 || end <= start)
        {
            return null;
        }

        var text = summary[(start + open.Length)..end].Trim();
        return text.Length == 0 ? null : text;
    }

    private static bool WantsLine(string line)
    {
        const string prefix = "{\"type\":\"";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return true;
        }

        var end = line.IndexOf('"', prefix.Length);
        return end < 0 || WantedTypes.Contains(line[prefix.Length..end]);
    }

    private static bool IsBlocking(JsonElement? output) =>
        output is { ValueKind: JsonValueKind.Object } o
        && (IsOneOf(o.Str("permissionDecision"), "deny", "block")
            || IsOneOf(o.Str("decision"), "deny", "block")
            || o.Bool("continue") == false);

    private static bool IsOneOf(string? value, params string[] candidates) =>
        value is not null && candidates.Contains(value, StringComparer.OrdinalIgnoreCase);

    private static TokenUsage ToTokens(JsonElement? usage)
    {
        var cacheRead = usage.Num("cacheReadTokens");
        var cacheWrite = usage.Num("cacheWriteTokens");
        // Copilot's inputTokens includes the cached tokens; report the non-cached part as "input".
        return new TokenUsage(Math.Max(0, usage.Num("inputTokens") - cacheRead - cacheWrite), usage.Num("outputTokens"), cacheRead, cacheWrite, usage.Num("reasoningTokens"));
    }

    private WorkspaceInfo ReadWorkspace(string? path)
    {
        if (path is null)
        {
            return new WorkspaceInfo(null, null, null, null);
        }

        try
        {
            var values = new DeserializerBuilder().Build().Deserialize<Dictionary<string, object?>>(fs.File.ReadAllText(path)) ?? [];
            string? Text(string key) => values.TryGetValue(key, out var v) ? v?.ToString() : null;
            DateTimeOffset? created = DateTimeOffset.TryParse(Text("created_at"), out var c) ? c : null;
            return new WorkspaceInfo(Text("cwd"), Text("name"), Text("summary"), created);
        }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or IOException)
        {
            logger.LogWarning("Could not read workspace.yaml ({ExceptionType})", ex.GetType().Name);
            return new WorkspaceInfo(null, null, null, null);
        }
    }

    internal static string? LeafOf(string? cwd) =>
        string.IsNullOrWhiteSpace(cwd) ? null : cwd.TrimEnd('\\', '/').Split('\\', '/').LastOrDefault();

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private sealed record WorkspaceInfo(string? Cwd, string? Name, string? Summary, DateTimeOffset? Created);
}



