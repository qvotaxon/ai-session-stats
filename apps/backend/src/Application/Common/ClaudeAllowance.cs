using System;

namespace SessionInsights.Application.Common;

public sealed record ClaudeAllowanceWindow(double UsedPercentage, DateTimeOffset? ResetsAt);

public sealed record ClaudeAllowanceSnapshot(
    DateTimeOffset UpdatedAt,
    ClaudeAllowanceWindow? FiveHour,
    ClaudeAllowanceWindow? SevenDay);

public sealed record ClaudeAllowanceDto(
    string Status,
    DateTimeOffset? UpdatedAt,
    ClaudeAllowanceWindow? FiveHour,
    ClaudeAllowanceWindow? SevenDay,
    string? Message);

public interface IClaudeAllowanceReader
{
    ClaudeAllowanceDto Get();
}
