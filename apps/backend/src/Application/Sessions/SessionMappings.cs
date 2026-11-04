using System;
using System.Linq;
using SessionInsights.Domain.Reports;
using SessionInsights.Domain.Sessions;

namespace SessionInsights.Application.Sessions;

public static class SessionMappings
{
    public static string ToWire(this SessionSource source) => source == SessionSource.Copilot ? "copilot" : "claude";

    public static string ToWire(this ReportStatus status) => status.ToString().ToLowerInvariant();

    public static string ToWire(this GuardKind kind) => kind switch
    {
        GuardKind.BlockedHook => "blockedHook",
        GuardKind.PermissionDenial => "permissionDenial",
        _ => "hookFailure",
    };

    public static SessionSummaryDto ToSummaryDto(this Session s, ReportStatus? latestReport) => new(
        s.Id,
        s.Source.ToWire(),
        s.Title,
        s.Project,
        s.StartedAt,
        s.EndedAt,
        s.DurationSeconds,
        s.Models,
        s.Tokens.Total,
        s.ToolCallTotal,
        s.HookTotal,
        s.GuardTotal,
        s.SubagentTotal,
        latestReport?.ToWire());

    public static SessionDetailDto ToDetailDto(this Session s, ReportStatus? latestReport)
    {
        var summary = s.ToSummaryDto(latestReport);
        return new SessionDetailDto(
            summary.Id,
            summary.Source,
            summary.Title,
            summary.Project,
            summary.StartedAt,
            summary.EndedAt,
            summary.DurationSeconds,
            summary.Models,
            summary.TotalTokens,
            summary.ToolCallCount,
            summary.HookCount,
            summary.GuardCount,
            summary.SubagentCount,
            summary.LatestReportStatus,
            new TokensDto(s.Tokens.Input, s.Tokens.Output, s.Tokens.CacheRead, s.Tokens.CacheWrite, s.Tokens.Reasoning, s.Tokens.Total),
            [.. s.TokensByModel.Select(m => new ModelTokensDto(m.Model, m.Requests, m.Tokens.Input, m.Tokens.Output, m.Tokens.CacheRead, m.Tokens.CacheWrite, m.Tokens.Reasoning))],
            new ToolCallsDto(s.ToolCallTotal, s.ToolCallsSucceeded, s.ToolCallsFailed,
                [.. s.Tools.Select(t => new ToolDto(t.Name, t.Count, t.Failed, t.AvgDurationMs, t.TotalDurationMs))]),
            new HooksDto(s.HookTotal, s.HookFailed, [.. s.HookTypes.Select(h => new HookTypeDto(h.Type, h.Count, h.Failed, h.AvgDurationMs))]),
            new GuardsDto(
                s.GuardTotal,
                s.Guards.Where(g => g.Kind == GuardKind.BlockedHook).Sum(g => g.Count),
                s.Guards.Where(g => g.Kind == GuardKind.PermissionDenial).Sum(g => g.Count),
                s.Guards.Where(g => g.Kind == GuardKind.HookFailure).Sum(g => g.Count),
                [.. s.Guards.Select(g => new GuardItemDto(g.Kind.ToWire(), g.Name, g.Count))]),
            new SubagentsDto(s.SubagentTotal, [.. s.SubagentTypes.Select(n => new NamedCountDto(n.Name, n.Count))]),
            [.. s.Skills.Select(n => new NamedCountDto(n.Name, n.Count))],
            new TurnsDto(s.UserTurns, s.AssistantTurns),
            new ApiDto(s.ApiRequestCount, s.ApiDurationMs),
            s.Cost is null ? null : new CostDto(s.Cost.PremiumRequests, s.Cost.NanoAiu),
            s.CodeChanges is null ? null : new CodeChangesDto(s.CodeChanges.LinesAdded, s.CodeChanges.LinesRemoved, s.CodeChanges.FilesModified),
            [.. s.Timeline.Select(b => new TimelineBucketDto(b.BucketStart, b.ToolCalls, b.Hooks, b.Guards, b.Tokens))],
            s.Notes);
    }
}
