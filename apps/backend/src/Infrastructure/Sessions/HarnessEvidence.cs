using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using SessionInsights.Domain.Sessions;

namespace SessionInsights.Infrastructure.Sessions;

internal static class HarnessEvidence
{
    private static readonly Regex VersionPattern = new(
        @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex ClaudePath = new(
        @"(?:^|[\\/])msdc-harness[\\/](?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?)[\\/]",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static string? Version(string? value) =>
        value is { Length: > 0 and <= 120 } && VersionPattern.IsMatch(value) ? value : null;

    public static bool IsPlugin(string? name) =>
        name is "msdc-harness" or "msdc-harness@msdc-plugins";

    public static HarnessObservation? Claude(JsonElement? attachment, DateTimeOffset? at, string? cliVersion)
    {
        if (attachment.Str("hookEvent") != "SessionStart")
        {
            return null;
        }

        var stdout = attachment.Str("stdout");
        var versions = stdout is null
            ? []
            : ClaudePath.Matches(stdout).Select(m => Version(m.Groups["version"].Value)).OfType<string>().Distinct().ToArray();
        return new HarnessObservation(at, "activation", versions.Length == 1 ? versions[0] : null,
            versions.Length switch { 0 => "unknown", 1 => "hookPath", _ => "conflicting" }, Version(cliVersion));
    }

    public static HarnessObservation? Copilot(JsonElement? data, DateTimeOffset? at, string? cliVersion) =>
        IsPlugin(data.Str("pluginName"))
            ? new HarnessObservation(at, "invocation", Version(data.Str("pluginVersion")),
                Version(data.Str("pluginVersion")) is null ? "unknown" : "invocation", Version(cliVersion))
            : null;

    public static IReadOnlyList<HarnessActivity> Attribute(
        IReadOnlyList<HarnessObservation> observations,
        IReadOnlyList<(DateTimeOffset? At, DateTimeOffset? StartedAt, bool Tool, bool Failed, bool Eligible)> events)
    {
        var activations = observations.Where(o => o.Kind == "activation").ToArray();
        var boundaries = activations.Where(o => o.At is not null).GroupBy(o => o.At!.Value).OrderBy(g => g.Key)
            .Select(g =>
            {
                var candidates = g.Select(o => (o.Version, o.CliVersion)).Distinct().ToArray();
                return (At: g.Key, Version: candidates.Length == 1 ? candidates[0].Version : null,
                    CliVersion: candidates.Length == 1 ? candidates[0].CliVersion : null);
            }).ToArray();
        var groups = new Dictionary<(string? Version, string? CliVersion), (int Tools, int FailedTools, int Hooks, int FailedHooks)>();
        var hasUntimedActivation = activations.Any(o => o.At is null);
        var boundaryTimes = boundaries.Select(b => b.At).ToHashSet();
        var index = -1;
        foreach (var e in events.OrderBy(e => e.At))
        {
            while (index + 1 < boundaries.Length && e.At is { } time && boundaries[index + 1].At < time)
            {
                index++;
            }

            // Same-timestamp events and subagents cannot reliably be assigned to a main-session activation.
            var known = e.Eligible && e.At is not null && index >= 0
                && !hasUntimedActivation && e.StartedAt > boundaries[index].At
                && e.StartedAt <= e.At && !boundaryTimes.Contains(e.At.Value);
            var version = known ? boundaries[index].Version : null;
            var key = (version, version is null ? null : boundaries[index].CliVersion);
            var current = groups.GetValueOrDefault(key);
            groups[key] = (current.Tools + (e.Tool ? 1 : 0), current.FailedTools + (e.Tool && e.Failed ? 1 : 0),
                current.Hooks + (e.Tool ? 0 : 1), current.FailedHooks + (!e.Tool && e.Failed ? 1 : 0));
        }

        return [.. groups.Select(g => new HarnessActivity(g.Key.Version, g.Key.CliVersion,
            g.Value.Tools, g.Value.FailedTools, g.Value.Hooks, g.Value.FailedHooks))];
    }
}
