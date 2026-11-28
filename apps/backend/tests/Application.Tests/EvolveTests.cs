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
    private readonly RecordingAnalyzer analyzer = new();

    [Fact]
    public async Task Project_combines_both_sources_and_filters_requests_not_sessions()
    {
        var a = Make("/a/demo", SessionSource.Copilot);
        var b = Make("/a/demo/", SessionSource.Claude);
        var unrelated = Make("/b/demo", SessionSource.Copilot);
        index.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<Session>>([a, b, unrelated]));
        var handler = new EvolveQueryHandler(index, analyzer);
        var ct = TestContext.Current.CancellationToken;

        var projects = await handler.Handle(new ListEvolveProjectsQuery(), ct);
        projects.Count.ShouldBe(2);
        var project = projects.Single(p => p.SessionCount == 2);
        analyzer.Result = new SkillAnalysis(2, 2, false,
        [
            new SkillOpportunity("skill", "Run tests", "Run the suite.", "# Run tests", 2, 2,
                [new SkillEvidence(a.Id, a.Title, 1), new SkillEvidence(b.Id, b.Title, 1)])
        ]);
        var result = await handler.Handle(new AnalyzeSkillsQuery(null, project.Id, Start, Start.AddHours(1), "claude", true), ct);

        result.SessionsScanned.ShouldBe(2);
        var suggestion = result.Opportunities.Single();
        suggestion.Id.ShouldBe("skill");
        suggestion.Occurrences.ShouldBe(2);
        suggestion.SessionCount.ShouldBe(2);
        // The handler passes the analyzer's evidence through untouched; ordering is the analyzer's job (see SkillAnalyzerTests).
        suggestion.Evidence.Select(e => e.SessionId).ShouldBe([a.Id, b.Id]);
        analyzer.Sessions.Select(s => s.Id).ShouldBe([a.Id, b.Id]);
        analyzer.From.ShouldBe(Start);
        analyzer.To.ShouldBe(Start.AddHours(1));
        analyzer.Cli.ShouldBe(SessionSource.Claude);
    }

    [Fact]
    public async Task Single_session_scope_and_date_range_are_passed_to_analyzer()
    {
        var session = Make(null, SessionSource.Claude);
        index.FindAsync(session.Id, Arg.Any<CancellationToken>()).Returns(new ValueTask<Session?>(session));
        var handler = new EvolveQueryHandler(index, analyzer);
        var ct = TestContext.Current.CancellationToken;

        await handler.Handle(new AnalyzeSkillsQuery(session.Id, null, null, null, "copilot", true), ct);
        analyzer.Sessions.Single().Id.ShouldBe(session.Id);
        analyzer.From.ShouldBeNull();
        analyzer.To.ShouldBeNull();
        analyzer.Cli.ShouldBe(SessionSource.Copilot);

        await handler.Handle(new AnalyzeSkillsQuery(session.Id, null, Start, null, "copilot", true), ct);
        analyzer.From.ShouldBe(Start);
    }

    [Fact]
    public void Invalid_scope_and_reversed_range_are_rejected()
    {
        var id = Guid.NewGuid().ToString();
        new AnalyzeSkillsQuery(null, null, null, null, "copilot", true).Validate().ShouldNotBeNull();
        new AnalyzeSkillsQuery(id, new string('a', 64), null, null, "copilot", true).Validate().ShouldNotBeNull();
        new AnalyzeSkillsQuery(id, null, Start.AddMinutes(1), Start, "copilot", true).Validate().ShouldNotBeNull();
        new AnalyzeSkillsQuery(id, null, null, null, "copilot", false).Validate().ShouldBe("Consent is required before selected prompts can be sent to the local CLI.");
        new AnalyzeSkillsQuery(id, null, null, null, "web", true).Validate().ShouldBe("Select the claude or copilot CLI.");
    }

    private static Session Make(string? cwd, SessionSource source) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Source = source,
        Cwd = cwd,
        Project = cwd is null ? null : "demo",
        StartedAt = Start.AddDays(-1),
    };

    private sealed class RecordingAnalyzer : ISkillAnalyzer
    {
        public IReadOnlyList<Session> Sessions { get; private set; } = [];
        public DateTimeOffset? From { get; private set; }
        public DateTimeOffset? To { get; private set; }
        public SessionSource Cli { get; private set; }
        public SkillAnalysisScope Scope { get; private set; }
        public SkillAnalysis Result { get; set; } = new(0, 0, false, []);

        public Task<SkillAnalysis> AnalyzeAsync(
            IReadOnlyList<Session> sessions,
            DateTimeOffset? from,
            DateTimeOffset? to,
            SessionSource cli,
            SkillAnalysisScope scope,
            CancellationToken cancellationToken)
        {
            Sessions = sessions;
            From = from;
            To = to;
            Cli = cli;
            Scope = scope;
            return Task.FromResult(Result);
        }
    }
}
