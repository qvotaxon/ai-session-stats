using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using SessionInsights.Application.Common;
using SessionInsights.Domain.Reports;
using SessionInsights.Domain.Sessions;
using SessionInsights.Infrastructure.Reports;
using SessionInsights.Infrastructure.Sessions;
using Xunit;
using static SessionInsights.Infrastructure.Tests.TestSupport;

namespace SessionInsights.Infrastructure.Tests;

public sealed class ForkExclusionTests
{
    private readonly MockFileSystem fs = new();

    private ExcludedSessionsStore Store() => new(fs, Options(), NullLogger<ExcludedSessionsStore>.Instance);

    private SessionIndex Index(IExcludedSessions excluded, IReportActivity activity) => new(
        [new ClaudeSessionReader(fs, Options(), NullLogger<ClaudeSessionReader>.Instance)],
        excluded,
        activity,
        Options(),
        TimeProvider.System,
        NullLogger<SessionIndex>.Instance);

    private void WriteClaude(string id, string firstMessage)
    {
        WriteLines(fs, $@"{ClaudeRoot}\proj\{id}.jsonl",
        [
            $$$"""{"type":"user","timestamp":"{{{Ts(0)}}}","cwd":"C:\\work","origin":{"kind":"human"},"message":{"content":"{{{firstMessage}}}"}}""",
            $$$"""{"type":"assistant","timestamp":"{{{Ts(1)}}}","message":{"id":"m","model":"x","usage":{"output_tokens":1},"content":[]}}""",
        ]);
    }

    [Fact]
    public void Excluded_ids_are_persisted_and_survive_a_restart()
    {
        var id = NewId();
        Store().Add(id);

        Store().Contains(id).ShouldBeTrue();
        Store().Contains(NewId()).ShouldBeFalse();
    }

    [Fact]
    public async Task Persisted_exclusion_hides_a_session_from_list_and_lookup()
    {
        var visible = NewId();
        var forked = NewId();
        WriteClaude(visible, "work");
        WriteClaude(forked, "work too");
        var store = Store();
        store.Add(forked);
        var index = Index(store, Substitute.For<IReportActivity>());

        var all = await index.GetAllAsync(TestContext.Current.CancellationToken);

        all.Select(s => s.Id).ShouldBe([visible]);
        (await index.FindAsync(forked, TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task First_message_heuristic_hides_a_fork_that_was_never_recorded()
    {
        var fork = NewId();
        WriteClaude(fork, ReportConstants.Prompt);
        var index = Index(Store(), Substitute.For<IReportActivity>());

        (await index.GetAllAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task New_sessions_are_held_back_while_a_report_run_is_active()
    {
        var existing = NewId();
        WriteClaude(existing, "work");
        var activity = Substitute.For<IReportActivity>();
        var index = Index(Store(), activity);
        (await index.GetAllAsync(TestContext.Current.CancellationToken)).Count.ShouldBe(1);

        var newcomer = NewId();
        WriteClaude(newcomer, "something that is not the prompt");
        activity.HasActiveRun.Returns(true);
        (await index.GetAllAsync(TestContext.Current.CancellationToken)).Select(s => s.Id).ShouldBe([existing]);

        activity.HasActiveRun.Returns(false);
        (await index.GetAllAsync(TestContext.Current.CancellationToken)).Count.ShouldBe(2);
    }
}

public sealed class ReportCoordinatorTests
{
    private const string Cwd = @"C:\work\demo";

    private sealed class FakeRunner : IProcessRunner
    {
        public List<ProcessSpec> Calls { get; } = [];
        public Func<ProcessSpec, CancellationToken, Task<ProcessResult>> Behaviour { get; set; } =
            (_, _) => Task.FromResult(new ProcessResult(0, "# Report", string.Empty));

        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
        {
            Calls.Add(spec);
            return Behaviour(spec, cancellationToken);
        }
    }

    private readonly MockFileSystem fs = new();
    private readonly FakeRunner runner = new();
    private readonly IExcludedSessions excluded = Substitute.For<IExcludedSessions>();
    private readonly ISessionIndex index = Substitute.For<ISessionIndex>();
    private readonly IExecutableLocator locator = Substitute.For<IExecutableLocator>();
    private readonly ReportActivityTracker tracker = new();

    public ReportCoordinatorTests()
    {
        fs.AddDirectory(Cwd);
        locator.Locate(Arg.Any<string>()).Returns(ci => @"C:\bin\" + ci.Arg<string>() + ".exe");
    }

    private ReportCoordinator Coordinator(TimeSpan? timeout = null) =>
        new(fs, index, excluded, runner, locator, tracker, Options(timeout), TimeProvider.System, NullLogger<ReportCoordinator>.Instance);

    private Session Given(SessionSource source)
    {
        var session = new Session { Id = NewId(), Source = source, Cwd = Cwd };
        index.FindAsync(session.Id, Arg.Any<CancellationToken>()).Returns(new ValueTask<Session?>(session));
        return session;
    }

    [Fact]
    public async Task Claude_run_forks_records_the_fork_and_never_uses_allow_all_flags()
    {
        var session = Given(SessionSource.Claude);
        var forkId = NewId();
        runner.Behaviour = (_, _) => Task.FromResult(new ProcessResult(0, $$"""{"result":"# Hello","session_id":"{{forkId}}","is_error":false}""", string.Empty));
        var coordinator = Coordinator();

        var run = await coordinator.StartAsync(session.Id, TestContext.Current.CancellationToken);
        await coordinator.ExecuteAsync(run.Id, TestContext.Current.CancellationToken);

        run.Status.ShouldBe(ReportStatus.Succeeded);
        run.ForkedSessionId.ShouldBe(forkId);
        excluded.Received().Add(forkId);
        var call = runner.Calls.Single();
        call.WorkingDirectory.ShouldBe(Cwd);
        call.Arguments.ShouldBe(["-p", ReportConstants.Prompt, "--resume", session.Id, "--fork-session", "--output-format", "json"]);
        call.Arguments.ShouldNotContain(a => a.Contains("dangerously") || a.Contains("allow-all"));
        (await coordinator.ReadContentAsync(run, TestContext.Current.CancellationToken)).ShouldBe("# Hello");
        tracker.HasActiveRun.ShouldBeFalse();
    }

    [Fact]
    public async Task Copilot_run_is_silent_and_captures_stdout()
    {
        var session = Given(SessionSource.Copilot);
        var coordinator = Coordinator();

        var run = await coordinator.StartAsync(session.Id, TestContext.Current.CancellationToken);
        await coordinator.ExecuteAsync(run.Id, TestContext.Current.CancellationToken);

        runner.Calls.Single().Arguments.ShouldBe(["-p", ReportConstants.Prompt, $"--resume={session.Id}", "-s"]);
        run.Status.ShouldBe(ReportStatus.Succeeded);
        run.Forked.ShouldBeFalse();
    }

    [Fact]
    public async Task Second_run_for_the_same_session_conflicts_while_the_first_is_active()
    {
        var session = Given(SessionSource.Copilot);
        var coordinator = Coordinator();
        await coordinator.StartAsync(session.Id, TestContext.Current.CancellationToken);

        await Should.ThrowAsync<ConflictException>(() => coordinator.StartAsync(session.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Versions_increment_per_session_after_a_run_finishes()
    {
        var session = Given(SessionSource.Copilot);
        var coordinator = Coordinator();
        var first = await coordinator.StartAsync(session.Id, TestContext.Current.CancellationToken);
        await coordinator.ExecuteAsync(first.Id, TestContext.Current.CancellationToken);

        var second = await coordinator.StartAsync(session.Id, TestContext.Current.CancellationToken);

        second.Version.ShouldBe(2);
    }

    [Fact]
    public async Task Cancelling_a_running_run_kills_it_and_marks_it_cancelled()
    {
        var session = Given(SessionSource.Copilot);
        var started = new TaskCompletionSource();
        runner.Behaviour = async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new ProcessResult(0, string.Empty, string.Empty);
        };
        var coordinator = Coordinator();
        var run = await coordinator.StartAsync(session.Id, TestContext.Current.CancellationToken);

        var execution = coordinator.ExecuteAsync(run.Id, TestContext.Current.CancellationToken);
        await started.Task;
        coordinator.Cancel(session.Id, run.Id);
        await execution;

        run.Status.ShouldBe(ReportStatus.Cancelled);
        tracker.HasActiveRun.ShouldBeFalse();
    }

    [Fact]
    public async Task Cancelling_a_finished_run_conflicts()
    {
        var session = Given(SessionSource.Copilot);
        var coordinator = Coordinator();
        var run = await coordinator.StartAsync(session.Id, TestContext.Current.CancellationToken);
        await coordinator.ExecuteAsync(run.Id, TestContext.Current.CancellationToken);

        Should.Throw<ConflictException>(() => coordinator.Cancel(session.Id, run.Id));
    }

    [Fact]
    public async Task A_run_that_exceeds_the_timeout_fails()
    {
        var session = Given(SessionSource.Copilot);
        runner.Behaviour = async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new ProcessResult(0, string.Empty, string.Empty);
        };
        var coordinator = Coordinator(TimeSpan.FromMilliseconds(50));
        var run = await coordinator.StartAsync(session.Id, TestContext.Current.CancellationToken);

        await coordinator.ExecuteAsync(run.Id, TestContext.Current.CancellationToken);

        run.Status.ShouldBe(ReportStatus.Failed);
        run.Error.ShouldNotBeNull().ShouldContain("timed out");
    }

    [Fact]
    public async Task Runs_active_at_shutdown_become_failed_interrupted_on_restart()
    {
        var session = Given(SessionSource.Copilot);
        var before = Coordinator();
        var run = await before.StartAsync(session.Id, TestContext.Current.CancellationToken);

        var after = Coordinator();

        var reloaded = after.Find(session.Id, run.Id).ShouldNotBeNull();
        reloaded.Status.ShouldBe(ReportStatus.Failed);
        reloaded.Error.ShouldBe("interrupted");
    }

    [Fact]
    public async Task Starting_a_run_for_an_unknown_or_excluded_session_is_not_found()
    {
        index.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ValueTask<Session?>((Session?)null));

        await Should.ThrowAsync<NotFoundException>(() => Coordinator().StartAsync(NewId(), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("1; calc.exe")]
    [InlineData("--resume")]
    public async Task Non_guid_session_ids_are_rejected_before_any_process_is_built(string id)
    {
        await Should.ThrowAsync<RequestValidationException>(() => Coordinator().StartAsync(id, TestContext.Current.CancellationToken));
        runner.Calls.ShouldBeEmpty();
    }
}
