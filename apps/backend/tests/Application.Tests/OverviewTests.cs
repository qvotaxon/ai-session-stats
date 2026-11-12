using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Shouldly;
using SessionInsights.Application.Common;
using SessionInsights.Application.Sessions;
using SessionInsights.Domain.Sessions;
using Xunit;

namespace SessionInsights.Application.Tests;

public sealed class OverviewTests
{
    [Fact]
    public void Session_discovery_uses_configured_cli_homes_unless_transcript_roots_are_explicit()
    {
        var options = new SessionInsightsOptions
        {
            ClaudeConfigDir = Path.Combine(Path.GetTempPath(), "custom-claude"),
            CopilotHome = Path.Combine(Path.GetTempPath(), "custom-copilot"),
        };
        options.ResolveClaudeRoot().ShouldBe(Path.Combine(options.ClaudeConfigDir, "projects"));
        options.ResolveCopilotRoot().ShouldBe(Path.Combine(options.CopilotHome, "session-state"));
        options.ClaudeRoot = Path.Combine(Path.GetTempPath(), "explicit-claude");
        options.CopilotRoot = Path.Combine(Path.GetTempPath(), "explicit-copilot");
        options.ResolveClaudeRoot().ShouldBe(options.ClaudeRoot);
        options.ResolveCopilotRoot().ShouldBe(options.CopilotRoot);
    }

    [Fact]
    public async Task Overview_aggregates_all_sessions_keeps_versions_and_providers_separate_and_counts_unknowns()
    {
        var index = Substitute.For<ISessionIndex>();
        var reports = Substitute.For<IReportCoordinator>();
        var now = DateTimeOffset.UtcNow;
        var sessions = Enumerable.Range(0, 110).Select(i => new Session
        {
            Id = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Source = i == 0 ? SessionSource.Claude : SessionSource.Copilot,
            LastActivityAt = now.AddDays(-i),
            Tokens = new TokenUsage(10, 0, 0, 0, 0),
            ToolCallsSucceeded = 1,
            Harness = i > 1 ? HarnessInfo.Empty : new HarnessInfo(
            [
                new HarnessObservation(now, i == 0 ? "activation" : "invocation", "0.26.1", i == 0 ? "hookPath" : "invocation", "1.0.0"),
                new HarnessObservation(now, "invocation", null, "unknown", "1.0.0"),
            ], i == 0 ? [new HarnessActivity("0.26.1", "1.0.0", 1, 0, 0, 0)] : []),
        }).ToArray();
        index.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<Session>>(sessions));

        var result = await new GetOverviewQueryHandler(index, reports, TimeProvider.System)
            .Handle(new GetOverviewQuery(), TestContext.Current.CancellationToken);

        result.TotalSessions.ShouldBe(110);
        result.TotalTokens.ShouldBe(1100);
        result.RecentSessions.Select(s => s.Id).ShouldBe(["0", "1", "2"]);
        result.SessionsWithVersionEvidence.ShouldBe(2);
        result.AttributedHealthEvents.ShouldBe(1);
        result.TotalHealthEvents.ShouldBe(110);
        result.HarnessVersions.Count.ShouldBe(2);
        result.HarnessVersions.Single(v => v.Source == "copilot").ToolCalls.ShouldBe(0);
        result.HarnessVersions.Single(v => v.Source == "claude").ToolCalls.ShouldBe(1);
        result.HarnessVersions.Single(v => v.Source == "claude").SessionIds.ShouldBe(["0"]);
    }
}
