using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using SessionInsights.Application.Common;

namespace SessionInsights.Application.Sessions;

public sealed record GetOverviewQuery : IQuery<OverviewDto>;

public sealed record HarnessVersionDto(
    string Source,
    string Version,
    string? CliVersion,
    int Sessions,
    int DirectInvocations,
    int Activations,
    int ToolCalls,
    int ToolFailures,
    int Hooks,
    int HookFailures,
    IReadOnlyList<string> SessionIds);

public sealed record OverviewDto(
    DateTimeOffset GeneratedAt,
    int TotalSessions,
    int RecentlyActiveSessions,
    long TotalTokens,
    int ToolCalls,
    int ToolFailures,
    int Hooks,
    int HookFailures,
    int SessionsWithVersionEvidence,
    int AttributedHealthEvents,
    int TotalHealthEvents,
    IReadOnlyList<SessionSummaryDto> RecentSessions,
    IReadOnlyList<HarnessVersionDto> HarnessVersions);

public sealed class GetOverviewQueryHandler(ISessionIndex index, IReportCoordinator reports, TimeProvider clock)
    : IQueryHandler<GetOverviewQuery, OverviewDto>
{
    public async ValueTask<OverviewDto> Handle(GetOverviewQuery query, CancellationToken cancellationToken)
    {
        var sessions = await index.GetAllAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var observations = sessions.SelectMany(s => s.Harness.Observations.Where(o => o.Version is not null)
            .Select(o => (Session: s, Observation: o))).ToArray();
        var versions = observations.GroupBy(x => (x.Session.Source, x.Observation.Version, x.Observation.CliVersion))
            .Select(g =>
            {
                var matching = g.Select(x => x.Session).DistinctBy(s => s.Id).ToArray();
                var activity = matching.SelectMany(s => s.Harness.Activity)
                    .Where(a => a.Version == g.Key.Version && a.CliVersion == g.Key.CliVersion).ToArray();
                return new HarnessVersionDto(
                    g.Key.Source.ToWire(), g.Key.Version!, g.Key.CliVersion, matching.Length,
                    g.Count(x => x.Observation.Kind == "invocation"),
                    g.Count(x => x.Observation.Kind == "activation"),
                    activity.Sum(a => a.ToolCalls), activity.Sum(a => a.ToolFailures),
                    activity.Sum(a => a.Hooks), activity.Sum(a => a.HookFailures),
                    [.. matching.OrderByDescending(s => s.LastActivityAt).ThenBy(s => s.Id, StringComparer.Ordinal).Select(s => s.Id)]);
            }).OrderBy(v => v.Source, StringComparer.Ordinal).ThenBy(v => v.Version, StringComparer.Ordinal)
            .ThenBy(v => v.CliVersion, StringComparer.Ordinal).ToArray();

        return new OverviewDto(
            now, sessions.Count,
            sessions.Count(s => s.LastActivityAt >= now.AddDays(-7) && s.LastActivityAt <= now),
            sessions.Sum(s => s.Tokens.Total),
            sessions.Sum(s => s.ToolCallTotal), sessions.Sum(s => s.ToolCallsFailed),
            sessions.Sum(s => s.HookTotal), sessions.Sum(s => s.HookFailed),
            sessions.Count(s => s.Harness.Observations.Any(o => o.Version is not null)),
            sessions.SelectMany(s => s.Harness.Activity).Where(a => a.Version is not null).Sum(a => a.ToolCalls + a.Hooks),
            sessions.Sum(s => s.ToolCallTotal + s.HookTotal),
            [.. sessions.OrderByDescending(s => s.LastActivityAt).ThenBy(s => s.Id, StringComparer.Ordinal).Take(3)
                .Select(s => s.ToSummaryDto(reports.LatestStatus(s.Id)))],
            versions);
    }
}
