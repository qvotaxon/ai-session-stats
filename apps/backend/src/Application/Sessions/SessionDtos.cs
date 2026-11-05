using System;
using System.Collections.Generic;

namespace SessionInsights.Application.Sessions;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public record SessionSummaryDto(
    string Id,
    string Source,
    string Title,
    string? Project,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActivityAt,
    DateTimeOffset? EndedAt,
    long DurationSeconds,
    IReadOnlyList<string> Models,
    long TotalTokens,
    int ToolCallCount,
    int HookCount,
    int GuardCount,
    int SubagentCount,
    string? LatestReportStatus);

public sealed record SessionDetailDto(
    string Id,
    string Source,
    string Title,
    string? Project,
    string? Recap,
    DateTimeOffset? RecapAt,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActivityAt,
    DateTimeOffset? EndedAt,
    long DurationSeconds,
    IReadOnlyList<string> Models,
    long TotalTokens,
    int ToolCallCount,
    int HookCount,
    int GuardCount,
    int SubagentCount,
    string? LatestReportStatus,
    TokensDto Tokens,
    IReadOnlyList<ModelTokensDto> TokensByModel,
    ToolCallsDto ToolCalls,
    HooksDto Hooks,
    GuardsDto Guards,
    SubagentsDto Subagents,
    IReadOnlyList<NamedCountDto> Skills,
    TurnsDto Turns,
    ApiDto Api,
    CostDto? Cost,
    CodeChangesDto? CodeChanges,
    IReadOnlyList<TimelineBucketDto> Timeline,
    IReadOnlyList<string> Notes,
    bool CanContinue);

public sealed record TokensDto(long Input, long Output, long CacheRead, long CacheWrite, long Reasoning, long Total);

public sealed record ModelTokensDto(string Model, long Requests, long Input, long Output, long CacheRead, long CacheWrite, long Reasoning);

public sealed record ToolDto(string Name, int Count, int Failed, long AvgDurationMs, long TotalDurationMs);

public sealed record ToolCallsDto(int Total, int Succeeded, int Failed, IReadOnlyList<ToolDto> ByTool);

public sealed record HookTypeDto(string Type, int Count, int Failed, long AvgDurationMs);

public sealed record HooksDto(int Total, int Failed, IReadOnlyList<HookTypeDto> ByType);

public sealed record GuardItemDto(string Kind, string Name, int Count);

public sealed record GuardsDto(int Total, int BlockedHooks, int PermissionDenials, int HookFailures, IReadOnlyList<GuardItemDto> Items);

public sealed record NamedCountDto(string Name, int Count);

public sealed record SubagentsDto(int Total, IReadOnlyList<NamedCountDto> ByType);

public sealed record TurnsDto(int User, int Assistant);

public sealed record ApiDto(long RequestCount, long TotalApiDurationMs);

public sealed record CostDto(double PremiumRequests, double NanoAiu);

public sealed record CodeChangesDto(long LinesAdded, long LinesRemoved, long FilesModified);

public sealed record TimelineBucketDto(DateTimeOffset BucketStart, int ToolCalls, int Hooks, int Guards, long Tokens);
