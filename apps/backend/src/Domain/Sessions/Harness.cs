using System;
using System.Collections.Generic;

namespace SessionInsights.Domain.Sessions;

public sealed record HarnessObservation(
    DateTimeOffset? At,
    string Kind,
    string? Version,
    string Evidence,
    string? CliVersion);

public sealed record HarnessActivity(
    string? Version,
    string? CliVersion,
    int ToolCalls,
    int ToolFailures,
    int Hooks,
    int HookFailures);

public sealed record HarnessInfo(
    IReadOnlyList<HarnessObservation> Observations,
    IReadOnlyList<HarnessActivity> Activity)
{
    public static HarnessInfo Empty { get; } = new([], []);
}
