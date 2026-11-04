using System;

namespace SessionInsights.Domain.Sessions;

public enum SessionSource
{
    Copilot,
    Claude,
}

public enum GuardKind
{
    BlockedHook,
    PermissionDenial,
    HookFailure,
}

public sealed record TokenUsage(long Input, long Output, long CacheRead, long CacheWrite, long Reasoning)
{
    public static TokenUsage Empty { get; } = new(0, 0, 0, 0, 0);

    /// <summary>Reasoning tokens are a subset of output tokens and are not added again.</summary>
    public long Total => Input + Output + CacheRead + CacheWrite;

    public TokenUsage Plus(TokenUsage other) =>
        new(Input + other.Input, Output + other.Output, CacheRead + other.CacheRead, CacheWrite + other.CacheWrite, Reasoning + other.Reasoning);

    public TokenUsage MaxWith(TokenUsage other) =>
        new(
            Math.Max(Input, other.Input),
            Math.Max(Output, other.Output),
            Math.Max(CacheRead, other.CacheRead),
            Math.Max(CacheWrite, other.CacheWrite),
            Math.Max(Reasoning, other.Reasoning));

    public TokenUsage MinusClamped(TokenUsage other) =>
        new(
            Math.Max(0, Input - other.Input),
            Math.Max(0, Output - other.Output),
            Math.Max(0, CacheRead - other.CacheRead),
            Math.Max(0, CacheWrite - other.CacheWrite),
            Math.Max(0, Reasoning - other.Reasoning));
}

public sealed record ModelUsage(string Model, long Requests, TokenUsage Tokens);

public sealed record ToolStat(string Name, int Count, int Failed, long TotalDurationMs)
{
    public long AvgDurationMs => Count == 0 ? 0 : TotalDurationMs / Count;
}

public sealed record HookStat(string Type, int Count, int Failed, long TotalDurationMs)
{
    public long AvgDurationMs => Count == 0 ? 0 : TotalDurationMs / Count;
}

public sealed record GuardItem(GuardKind Kind, string Name, int Count);

public sealed record NamedCount(string Name, int Count);

public sealed record TimelineBucket(DateTimeOffset BucketStart, int ToolCalls, int Hooks, int Guards, long Tokens);

public sealed record CostInfo(double PremiumRequests, double NanoAiu);

public sealed record CodeChangeInfo(long LinesAdded, long LinesRemoved, long FilesModified);
