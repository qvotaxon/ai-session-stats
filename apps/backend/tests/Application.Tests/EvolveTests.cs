using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using SessionInsights.Application.Common;
using SessionInsights.Application.Sessions;
using SessionInsights.Domain.Sessions;
using Shouldly;
using Xunit;

namespace SessionInsights.Application.Tests;

public sealed class EvolveTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 10, 10, 0, 0, TimeSpan.Zero);
    private readonly ISessionIndex index = Substitute.For<ISessionIndex>();

    [Fact]
    public async Task Project_combines_both_sources_and_filters_requests_not_sessions()
    {
        var a = Make("/a/demo", SessionSource.Copilot, Start.AddMinutes(-1), Start, null);
        var b = Make("/a/demo/", SessionSource.Claude, Start.AddHours(1), Start.AddHours(2));
        var unrelated = Make("/b/demo", SessionSource.Copilot, Start, Start);
        index.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<Session>>([a, b, unrelated]));
        var handler = new EvolveQueryHandler(index);
        var ct = TestContext.Current.CancellationToken;

        var projects = await handler.Handle(new ListEvolveProjectsQuery(), ct);
        projects.Count.ShouldBe(2);
        var project = projects.Single(p => p.SessionCount == 2);
        var result = await handler.Handle(new AnalyzeSkillsQuery(null, project.Id, Start, Start.AddHours(1)), ct);

        result.SessionsScanned.ShouldBe(2);
        var suggestion = result.Opportunities.Single();
        suggestion.Id.ShouldBe("run-tests");
        suggestion.Occurrences.ShouldBe(2);
        suggestion.SessionCount.ShouldBe(2);
        suggestion.Evidence.Select(e => e.SessionId).ShouldBe([.. new[] { a.Id, b.Id }.Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public async Task Single_session_requires_recurrence_and_can_include_undated_requests()
    {
        var session = Make(null, SessionSource.Claude, Start, null);
        index.FindAsync(session.Id, Arg.Any<CancellationToken>()).Returns(new ValueTask<Session?>(session));
        var handler = new EvolveQueryHandler(index);
        var ct = TestContext.Current.CancellationToken;

        (await handler.Handle(new AnalyzeSkillsQuery(session.Id, null, null, null), ct))
            .Opportunities.Single().Occurrences.ShouldBe(2);
        (await handler.Handle(new AnalyzeSkillsQuery(session.Id, null, Start, null), ct))
            .Opportunities.ShouldBeEmpty();
    }

    [Fact]
    public void Invalid_scope_and_reversed_range_are_rejected()
    {
        var id = Guid.NewGuid().ToString();
        new AnalyzeSkillsQuery(null, null, null, null).Validate().ShouldNotBeNull();
        new AnalyzeSkillsQuery(id, new string('a', 64), null, null).Validate().ShouldNotBeNull();
        new AnalyzeSkillsQuery(id, null, Start.AddMinutes(1), Start).Validate().ShouldNotBeNull();
    }

    private static Session Make(string? cwd, SessionSource source, params DateTimeOffset?[] times) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Source = source,
        Cwd = cwd,
        Project = cwd is null ? null : "demo",
        StartedAt = Start.AddDays(-1),
        SkillSignals = [.. times.Select(t => new SkillSignal("run-tests", t))],
    };
}
