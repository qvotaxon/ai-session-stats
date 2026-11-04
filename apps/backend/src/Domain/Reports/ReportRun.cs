using System;
using SessionInsights.Domain.Sessions;

namespace SessionInsights.Domain.Reports;

public enum ReportStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

public sealed class ReportRun
{
    public required string Id { get; init; }
    public required string SessionId { get; init; }
    public required int Version { get; init; }
    public ReportStatus Status { get; set; }
    public required SessionSource Cli { get; init; }
    public bool Forked { get; init; }
    public string? ForkedSessionId { get; set; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; set; }
    public long DurationMs { get; set; }
    public int? ExitCode { get; set; }
    public string? Error { get; set; }

    public bool IsActive => Status is ReportStatus.Queued or ReportStatus.Running;
}

public static class ReportConstants
{
    public const string Prompt = "Give me a session report in markdown format";
}
