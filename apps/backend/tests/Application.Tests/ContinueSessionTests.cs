using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using SessionInsights.Application.Common;
using SessionInsights.Application.Terminals;
using SessionInsights.Domain.Reports;
using SessionInsights.Domain.Sessions;
using Xunit;

namespace SessionInsights.Application.Tests;

public sealed class ContinueSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private readonly ISessionIndex index = Substitute.For<ISessionIndex>();
    private readonly IReportCoordinator reports = Substitute.For<IReportCoordinator>();
    private readonly ITerminalSettings settings = Substitute.For<ITerminalSettings>();
    private readonly ITerminalLauncher launcher = Substitute.For<ITerminalLauncher>();
    private readonly TimeProvider time = new FixedTime(Now);

    public ContinueSessionTests()
    {
        settings.Terminal.Returns(TerminalOptions.WtPwsh);
        launcher.Available().Returns([TerminalOptions.WtPwsh, TerminalOptions.Cmd]);
        launcher.FolderExists(Arg.Any<string>()).Returns(true);
        reports.List(Arg.Any<string>()).Returns([]);
    }

    private ContinueSessionCommandHandler Handler(Session session)
    {
        index.FindAsync(session.Id, Arg.Any<CancellationToken>()).Returns(new ValueTask<Session?>(session));
        return new ContinueSessionCommandHandler(
            index, reports, settings, launcher,
            Options.Create(new SessionInsightsOptions { CopilotCliPath = "copilot", ClaudeCliPath = "claude" }), time);
    }

    private static Session Make(SessionSource source, TimeSpan idle, string? cwd = @"C:\work\repo") => new()
    {
        Id = Guid.NewGuid().ToString("D"),
        Source = source,
        Cwd = cwd,
        LastActivityAt = Now - idle,
    };

    private static Task Run(ContinueSessionCommandHandler handler, Session session, bool force = false) =>
        handler.Handle(new ContinueSessionCommand(session.Id, force), TestContext.Current.CancellationToken).AsTask();

    [Fact]
    public async Task Copilot_session_resumes_with_equals_syntax_in_session_folder()
    {
        var session = Make(SessionSource.Copilot, TimeSpan.FromHours(1));

        await Run(Handler(session), session);

        launcher.Received(1).Launch(TerminalOptions.WtPwsh, @"C:\work\repo", "copilot", Arg.Is<IReadOnlyList<string>>(a => a.Count == 1 && a[0] == $"--resume={session.Id}"));
    }

    [Fact]
    public async Task Claude_session_resumes_with_separate_argument()
    {
        var session = Make(SessionSource.Claude, TimeSpan.FromHours(1));

        await Run(Handler(session), session);

        launcher.Received(1).Launch(TerminalOptions.WtPwsh, @"C:\work\repo", "claude", Arg.Is<IReadOnlyList<string>>(a => a.Count == 2 && a[0] == "--resume" && a[1] == session.Id));
    }

    [Fact]
    public async Task Unavailable_terminal_falls_back_to_default()
    {
        settings.Terminal.Returns(TerminalOptions.WtCmd);
        launcher.Available().Returns([TerminalOptions.Pwsh, TerminalOptions.WtPwsh]);
        var session = Make(SessionSource.Claude, TimeSpan.FromHours(1));

        await Run(Handler(session), session);

        launcher.Received(1).Launch(TerminalOptions.WtPwsh, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>());
    }

    [Fact]
    public async Task Recently_active_session_needs_force()
    {
        var session = Make(SessionSource.Claude, TimeSpan.FromMinutes(1));
        var handler = Handler(session);

        var ex = await Should.ThrowAsync<ConflictException>(() => Run(handler, session));
        ex.Code.ShouldBe(ContinueSessionCommandHandler.SessionActiveCode);

        await Run(handler, session, force: true);
        launcher.Received(1).Launch(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>());
    }

    [Fact]
    public async Task Running_report_blocks_even_with_force()
    {
        var session = Make(SessionSource.Claude, TimeSpan.FromHours(1));
        reports.List(session.Id).Returns([new ReportRun { Id = "r", SessionId = session.Id, Version = 1, Cli = SessionSource.Claude, Status = ReportStatus.Running }]);

        var ex = await Should.ThrowAsync<ConflictException>(() => Run(Handler(session), session, force: true));

        ex.Code.ShouldBeNull();
        launcher.DidNotReceiveWithAnyArgs().Launch(default!, default!, default!, default!);
    }

    [Fact]
    public async Task Missing_folder_is_unprocessable()
    {
        launcher.FolderExists(Arg.Any<string>()).Returns(false);
        var session = Make(SessionSource.Copilot, TimeSpan.FromHours(1));

        await Should.ThrowAsync<UnprocessableException>(() => Run(Handler(session), session));
    }

    [Fact]
    public async Task Unknown_session_is_not_found()
    {
        index.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ValueTask<Session?>((Session?)null));
        var handler = new ContinueSessionCommandHandler(index, reports, settings, launcher, Options.Create(new SessionInsightsOptions()), time);

        await Should.ThrowAsync<NotFoundException>(async () =>
            await handler.Handle(new ContinueSessionCommand(Guid.NewGuid().ToString("D"), false), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("wt-pwsh", true)]
    [InlineData("cmd", true)]
    [InlineData("pwsh -c calc", false)]
    [InlineData("", false)]
    public void Only_known_terminals_are_accepted(string terminal, bool valid) =>
        (new UpdateSettingsCommand(terminal).Validate() is null).ShouldBe(valid);

    [Fact]
    public void Invalid_session_id_is_rejected() => new ContinueSessionCommand("..\\x", false).Validate().ShouldNotBeNull();
}
