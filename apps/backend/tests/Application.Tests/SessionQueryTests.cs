using System;
using System.Collections.Generic;
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

public sealed class SessionQueryTests
{
    private readonly ISessionIndex index = Substitute.For<ISessionIndex>();
    private readonly IReportCoordinator reports = Substitute.For<IReportCoordinator>();

    private static Session Make(SessionSource source, string title, int minute, long tokens, int tools = 0) => new()
    {
        Id = Guid.NewGuid().ToString("D"),
        Source = source,
        Title = title,
        StartedAt = new DateTimeOffset(2026, 1, 10, 10, minute, 0, TimeSpan.Zero),
        Tokens = new TokenUsage(tokens, 0, 0, 0, 0),
        ToolCallsSucceeded = tools,
    };

    private ListSessionsQueryHandler Handler(params Session[] sessions)
    {
        index.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<Session>>(sessions));
        return new ListSessionsQueryHandler(index, reports);
    }

    [Fact]
    public async Task Default_sort_is_newest_first()
    {
        var handler = Handler(Make(SessionSource.Copilot, "old", 1, 1), Make(SessionSource.Claude, "new", 9, 1));

        var page = await handler.Handle(new ListSessionsQuery(), TestContext.Current.CancellationToken);

        page.Items.Select(i => i.Title).ShouldBe(["new", "old"]);
    }

    [Fact]
    public async Task Sorts_by_tokens_ascending()
    {
        var handler = Handler(Make(SessionSource.Copilot, "big", 1, 500), Make(SessionSource.Copilot, "small", 2, 5));

        var page = await handler.Handle(new ListSessionsQuery(Sort: "totalTokens", Direction: "asc"), TestContext.Current.CancellationToken);

        page.Items.Select(i => i.Title).ShouldBe(["small", "big"]);
    }

    [Fact]
    public async Task Filters_by_source_and_search_term()
    {
        var handler = Handler(
            Make(SessionSource.Copilot, "Dashboard work", 1, 1),
            Make(SessionSource.Claude, "Dashboard fix", 2, 1),
            Make(SessionSource.Claude, "Other", 3, 1));

        var page = await handler.Handle(new ListSessionsQuery(Source: "claude", Search: "dashboard"), TestContext.Current.CancellationToken);

        page.Items.Select(i => i.Title).ShouldBe(["Dashboard fix"]);
        page.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Paginates_and_clamps_page_size()
    {
        var handler = Handler([.. Enumerable.Range(0, 250).Select(i => Make(SessionSource.Copilot, $"s{i}", i % 60, i))]);

        var first = await handler.Handle(new ListSessionsQuery(Page: 1, PageSize: 1000), TestContext.Current.CancellationToken);
        var last = await handler.Handle(new ListSessionsQuery(Page: 3, PageSize: 100), TestContext.Current.CancellationToken);

        first.PageSize.ShouldBe(100);
        first.TotalPages.ShouldBe(3);
        first.TotalCount.ShouldBe(250);
        last.Items.Count.ShouldBe(50);
    }

    [Theory]
    [InlineData(0, 25, "startedAt", "desc")]
    [InlineData(1, 0, "startedAt", "desc")]
    [InlineData(1, 25, "bogus", "desc")]
    [InlineData(1, 25, "startedAt", "sideways")]
    public void Invalid_queries_are_rejected(int page, int pageSize, string sort, string direction) =>
        new ListSessionsQuery(page, pageSize, Sort: sort, Direction: direction).Validate().ShouldNotBeNull();

    [Fact]
    public void Unknown_source_is_rejected() => new ListSessionsQuery(Source: "vim").Validate().ShouldNotBeNull();

    [Theory]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301", true)]
    [InlineData("not-a-guid", false)]
    [InlineData("", false)]
    [InlineData("3f2504e04f8911d39a0c0305e82c3301", false)]
    [InlineData("{3f2504e0-4f89-11d3-9a0c-0305e82c3301}", false)]
    [InlineData("..\\..\\windows", false)]
    public void Guid_validation(string id, bool valid)
    {
        SessionIdFormat.IsValid(id).ShouldBe(valid);
        (new GetSessionQuery(id).Validate() is null).ShouldBe(valid);
    }

    [Fact]
    public async Task Missing_session_is_not_found()
    {
        index.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ValueTask<Session?>((Session?)null));
        var handler = new GetSessionQueryHandler(index, reports);

        await Should.ThrowAsync<NotFoundException>(async () => await handler.Handle(new GetSessionQuery(Guid.NewGuid().ToString("D")), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Titles_are_truncated_to_120_characters() => Session.TruncateTitle(new string('x', 500)).Length.ShouldBe(120);
}
