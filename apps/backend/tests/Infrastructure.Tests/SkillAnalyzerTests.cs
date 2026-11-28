using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SessionInsights.Application.Common;
using SessionInsights.Application.Sessions;
using SessionInsights.Domain.Sessions;
using SessionInsights.Infrastructure.Sessions;
using Shouldly;
using Xunit;
using static SessionInsights.Infrastructure.Tests.TestSupport;

namespace SessionInsights.Infrastructure.Tests;

public sealed class SkillAnalyzerTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 10, 10, 0, 0, TimeSpan.Zero);
    private static readonly Regex SafeArgument = new(@"^[A-Za-z0-9_.=,-]+\z");
    private readonly IExecutableLocator locator = Substitute.For<IExecutableLocator>();
    private readonly IProcessRunner processes = Substitute.For<IProcessRunner>();
    private readonly ISkillAnalysisActivity activity = new SkillAnalysisActivity();
    private readonly MockFileSystem fs = new();

    // One options instance serves the analyzer and the cleanup, as DI does. A rooted CopilotHome keeps the
    // simulated CLI and the cleanup independent of the host's COPILOT_HOME and profile.
    private readonly IOptions<SessionInsightsOptions> options = Options();
    private readonly CopilotEvolveSessionCleanup cleanup;

    public SkillAnalyzerTests()
    {
        options.Value.CopilotHome = CopilotHome;
        cleanup = new CopilotEvolveSessionCleanup(fs, options, NullLogger<CopilotEvolveSessionCleanup>.Instance);
    }

    private const string CopilotHome = @"C:\data\copilot-home";

    private SkillAnalyzer Analyzer(IEvolveSessionCleanup? cleanupOverride = null, IOptions<SessionInsightsOptions>? optionsOverride = null) =>
        new(locator, processes, activity, optionsOverride ?? options, cleanupOverride ?? cleanup, analyzerLog);

    private readonly ListLogger<SkillAnalyzer> analyzerLog = new();

    private static string SessionIdOf(ProcessSpec spec) => spec.Arguments[spec.Arguments.ToList().IndexOf("--session-id") + 1];

    private string SessionFolder(string id) => $@"{CopilotHome}\session-state\{id}";

    private void CreateSession(string id, string? cwd = null)
    {
        fs.AddDirectory(SessionFolder(id));
        fs.AddFile($@"{SessionFolder(id)}\events.jsonl", new MockFileData("{}"));
        fs.AddFile($@"{SessionFolder(id)}\workspace.yaml", new MockFileData($"id: {id}\ncwd: '{cwd ?? SkillAnalysisActivity.WorkingDirectory}'\n"));
    }

    private static Session[] TwoRequests(SessionSource source = SessionSource.Copilot) =>
    [
        new Session
        {
            Id = "session-a",
            Source = source,
            SkillPrompts = [new SkillPrompt("First repeated request", Start), new SkillPrompt("Second repeated request", Start.AddMinutes(1))],
        },
    ];

    private void CliCreatesSessionAndReturns(string stdout, string? cwd = null, int exitCode = 0) =>
        processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                CreateSession(SessionIdOf(call.Arg<ProcessSpec>()), cwd);
                return Task.FromResult(new ProcessResult(exitCode, stdout, string.Empty));
            });

    [Fact]
    public async Task Uses_local_cli_and_turns_repeated_requests_into_instructions_with_evidence()
    {
        locator.Locate("copilot").Returns("/usr/bin/copilot");
        ProcessSpec? captured = null;
        processes.RunAsync(Arg.Do<ProcessSpec>(spec => captured = spec), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ProcessResult(0,
                """{"opportunities":[{"title":"Release notes","description":"Repeated release requests.","instructions":"# Release notes\n\n1. Review merged changes.","requestIds":["R001","R002","R999"]}]}""",
                string.Empty)));
        var first = new Session
        {
            Id = "session-a",
            Source = SessionSource.Copilot,
            Title = "First session",
            SkillPrompts =
            [
                new SkillPrompt("An old request outside the selected range", Start),
                new SkillPrompt("Prepare release notes from the merged changes", Start.AddDays(1)),
            ],
        };
        var second = new Session
        {
            Id = "session-b",
            Source = SessionSource.Claude,
            Title = "Second session",
            SkillPrompts = [new SkillPrompt("Write release notes for the changes merged today", Start.AddDays(2))],
        };
        var analyzer = Analyzer();

        var result = await analyzer.AnalyzeAsync([first, second], Start.AddDays(1), null, SessionSource.Copilot, SkillAnalysisScope.Session,
            TestContext.Current.CancellationToken);

        result.SessionsScanned.ShouldBe(2);
        result.RequestsAnalyzed.ShouldBe(2);
        result.Sampled.ShouldBeFalse();
        var suggestion = result.Opportunities.Single();
        suggestion.Title.ShouldBe("Release notes");
        suggestion.Instructions.ShouldContain("# Release notes");
        suggestion.Occurrences.ShouldBe(2);
        suggestion.SessionCount.ShouldBe(2);
        suggestion.Evidence.Select(item => item.SessionId).Order(StringComparer.Ordinal).ShouldBe(["session-a", "session-b"]);
        await processes.Received(1).RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>());
        captured.ShouldNotBeNull();
        captured.FileName.ShouldBe("/usr/bin/copilot");
        captured.WorkingDirectory.ShouldBe(SkillAnalysisActivity.WorkingDirectory);
        captured.Arguments.Take(captured.Arguments.Count - 2).ShouldBe(
        [
            "--silent", "--no-custom-instructions", "--disable-builtin-mcps",
            "--disable-mcp-server", "computer-use", "--no-ask-user",
            "--available-tools=view", "--sync-session=off", "--disallow-temp-dir",
            "--deny-tool", "shell", "--deny-tool", "write", "--deny-tool", "read",
            "--deny-tool", "url", "--deny-tool", "memory",
        ]);
        captured.Arguments[^2].ShouldBe("--session-id");
        SessionIdFormat.IsValid(captured.Arguments[^1]).ShouldBeTrue();
        captured.RemoveEnvironment.ShouldBe(
        [
            "COPILOT_ALLOW_ALL", "COPILOT_ASSISTED_APPROVAL", "COPILOT_PLAN_THEN_AUTOPILOT",
            "GITHUB_COPILOT_PROMPT_MODE_EXTENSIONS", "GITHUB_COPILOT_PROMPT_MODE_REPO_HOOKS",
            "GITHUB_COPILOT_PROMPT_MODE_WORKSPACE_MCP", "COPILOT_SKILLS_DIRS", "COPILOT_CUSTOM_INSTRUCTIONS_DIRS",
            "COPILOT_OTEL_ENABLED", "COPILOT_OTEL_FILE_EXPORTER_PATH", "OTEL_EXPORTER_OTLP_ENDPOINT",
            "COPILOT_ENABLE_INTERRUPTED_SESSION_RESTORE",
        ]);
        captured.Environment.ShouldBe(new Dictionary<string, string> { ["COPILOT_HOME"] = CopilotHome });
        captured.Arguments.ShouldNotContain("-p");
        captured.Arguments.ShouldAllBe(argument => SafeArgument.IsMatch(argument));
        captured.Arguments.ShouldNotContain(argument => argument.Contains("Prepare release notes"));
        captured.StandardInput.ShouldNotBeNull();
        captured.StandardInput.ShouldStartWith("Find repeated task patterns");
        captured.StandardInput.ShouldContain("Prepare release notes");
        captured.StandardInput.ShouldNotContain("outside the selected range");
        captured.StandardInput.IndexOf("Analyze the JSON requests", StringComparison.Ordinal)
            .ShouldBeLessThan(captured.StandardInput.IndexOf("Prepare release notes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Copilot_keeps_configured_home_and_never_removes_auth_home_host_model_or_provider_variables()
    {
        locator.Locate("copilot").Returns("/usr/bin/copilot");
        ProcessSpec? captured = null;
        processes.RunAsync(Arg.Do<ProcessSpec>(spec => captured = spec), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ProcessResult(0, """{"opportunities":[]}""", string.Empty)));

        await Analyzer().AnalyzeAsync(TwoRequests(), null, null, SessionSource.Copilot, SkillAnalysisScope.Session, TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured.Environment.ShouldNotBeNull();
        captured.Environment.ShouldBe(new Dictionary<string, string> { ["COPILOT_HOME"] = CopilotHome });
        captured.RemoveEnvironment.ShouldNotBeNull();
        captured.RemoveEnvironment.ShouldNotContain("COPILOT_HOME");
        captured.RemoveEnvironment.ShouldNotContain("COPILOT_GITHUB_TOKEN");
        captured.RemoveEnvironment.ShouldNotContain("GH_TOKEN");
        captured.RemoveEnvironment.ShouldNotContain("GITHUB_TOKEN");
        captured.RemoveEnvironment.ShouldNotContain("COPILOT_GH_HOST");
        captured.RemoveEnvironment.ShouldNotContain("GH_HOST");
        captured.RemoveEnvironment.ShouldNotContain("COPILOT_MODEL");
        captured.RemoveEnvironment.ShouldNotContain("COPILOT_AUTO_UPDATE");
        captured.RemoveEnvironment.ShouldNotContain("COPILOT_CACHE_HOME");
        captured.RemoveEnvironment.ShouldNotContain("COPILOT_PROVIDERS_CONFIG");
        captured.RemoveEnvironment.ShouldNotContain("COPILOT_PROVIDER_API_KEY");
        captured.RemoveEnvironment.ShouldNotContain("COPILOT_PROVIDER_API_KEY_COMMAND");
        captured.RemoveEnvironment.ShouldNotContain("COPILOT_PROVIDER_HEADERS");
        captured.RemoveEnvironment.Intersect(captured.Environment.Keys, StringComparer.OrdinalIgnoreCase).ShouldBeEmpty();
    }

    [Fact]
    public async Task Copilot_passes_no_environment_when_no_home_is_configured()
    {
        locator.Locate("copilot").Returns("/usr/bin/copilot");
        ProcessSpec? captured = null;
        processes.RunAsync(Arg.Do<ProcessSpec>(spec => captured = spec), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ProcessResult(0, """{"opportunities":[]}""", string.Empty)));
        var noHome = Options();
        var noHomeCleanup = new CopilotEvolveSessionCleanup(fs, noHome, NullLogger<CopilotEvolveSessionCleanup>.Instance);

        await Analyzer(noHomeCleanup, noHome).AnalyzeAsync(TwoRequests(), null, null, SessionSource.Copilot, SkillAnalysisScope.Session, TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured.Environment.ShouldBeNull();
        captured.RemoveEnvironment.ShouldNotBeNull();
        captured.RemoveEnvironment.ShouldNotContain("COPILOT_HOME");
    }

    [Fact]
    public async Task Removes_its_own_copilot_session_after_the_run()
    {
        locator.Locate("copilot").Returns("/usr/bin/copilot");
        CliCreatesSessionAndReturns("""{"opportunities":[]}""");

        await Analyzer().AnalyzeAsync(TwoRequests(), null, null, SessionSource.Copilot, SkillAnalysisScope.Session, TestContext.Current.CancellationToken);

        fs.Directory.GetDirectories($@"{CopilotHome}\session-state").ShouldBeEmpty();
    }

    [Fact]
    public async Task Removes_its_session_when_the_cli_exits_nonzero()
    {
        locator.Locate("copilot").Returns("/usr/bin/copilot");
        CliCreatesSessionAndReturns(string.Empty, exitCode: 1);

        var failure = await Should.ThrowAsync<UnprocessableException>(() =>
            Analyzer().AnalyzeAsync(TwoRequests(), null, null, SessionSource.Copilot, SkillAnalysisScope.Session, TestContext.Current.CancellationToken));

        failure.Message.ShouldBe("The copilot CLI exited with code 1.");
        fs.Directory.GetDirectories($@"{CopilotHome}\session-state").ShouldBeEmpty();
    }

    [Fact]
    public async Task Removes_its_session_when_the_output_is_invalid()
    {
        locator.Locate("copilot").Returns("/usr/bin/copilot");
        CliCreatesSessionAndReturns("this is not json");

        var failure = await Should.ThrowAsync<UnprocessableException>(() =>
            Analyzer().AnalyzeAsync(TwoRequests(), null, null, SessionSource.Copilot, SkillAnalysisScope.Session, TestContext.Current.CancellationToken));

        failure.Message.ShouldBe("The selected CLI returned an invalid skill analysis. Try again.");
        fs.Directory.GetDirectories($@"{CopilotHome}\session-state").ShouldBeEmpty();
    }

    [Fact]
    public async Task Logs_a_diagnostic_without_the_model_output_when_the_output_cannot_be_parsed()
    {
        const string secret = "PROSE-FROM-THE-MODEL-42";
        locator.Locate("copilot").Returns("/usr/bin/copilot");
        CliCreatesSessionAndReturns($"I will look at the history. {secret}");

        await Should.ThrowAsync<UnprocessableException>(() =>
            Analyzer().AnalyzeAsync(TwoRequests(), null, null, SessionSource.Copilot, SkillAnalysisScope.Session, TestContext.Current.CancellationToken));

        var entry = analyzerLog.Entries.Where(e => e.Level == LogLevel.Warning).ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Properties["Cli"].ShouldBe(SessionSource.Copilot);
        entry.Properties.ContainsKey("ExitCode").ShouldBeFalse();
        entry.Properties["OutputLength"].ShouldBe($"I will look at the history. {secret}".Length);
        analyzerLog.AllText().ShouldNotContain(text => text.Contains(secret));
    }

    [Fact]
    public async Task Logs_one_information_line_with_sizes_and_outcome_but_no_request_text_or_output()
    {
        const string secret = "SECRET-REQUEST-TEXT";
        const string reply = "{\"opportunities\":[]}";
        locator.Locate("claude").Returns("/usr/bin/claude");
        processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ProcessResult(0, $$"""{"result":"{\"opportunities\":[]}"}""", string.Empty)));
        var session = new Session
        {
            Id = "session-a",
            Title = "TITLE-SECRET",
            Source = SessionSource.Claude,
            SkillPrompts = [new SkillPrompt(secret, Start), new SkillPrompt(secret, Start.AddMinutes(1))],
        };

        await Analyzer().AnalyzeAsync([session], null, null, SessionSource.Claude, SkillAnalysisScope.Project, TestContext.Current.CancellationToken);

        var entry = analyzerLog.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Properties["Cli"].ShouldBe("claude");
        entry.Properties["Scope"].ShouldBe("project");
        entry.Properties["RequestCount"].ShouldBe(2);
        entry.Properties["InputChars"].ShouldBe(secret.Length * 2);
        entry.Properties["Outcome"].ShouldBe("ok");
        entry.Properties["StdoutChars"].ShouldBe($$"""{"result":"{\"opportunities\":[]}"}""".Length);
        entry.Properties["OpportunityCount"].ShouldBe(0);
        entry.Properties["ElapsedMs"].ShouldBeOfType<long>();
        analyzerLog.AllText().ShouldNotContain(text => text.Contains(secret) || text.Contains("TITLE-SECRET") || text.Contains(reply));
    }

    [Fact]
    public async Task Logs_the_invalid_json_outcome_when_the_output_cannot_be_parsed()
    {
        locator.Locate("copilot").Returns("/usr/bin/copilot");
        CliCreatesSessionAndReturns("not json");

        await Should.ThrowAsync<UnprocessableException>(() =>
            Analyzer().AnalyzeAsync(TwoRequests(), null, null, SessionSource.Copilot, SkillAnalysisScope.Session, TestContext.Current.CancellationToken));

        var entry = analyzerLog.Entries.Single(e => e.Level == LogLevel.Information);
        entry.Properties["Outcome"].ShouldBe("invalid-json");
        entry.Properties["Scope"].ShouldBe("session");
        entry.Properties["StdoutChars"].ShouldBe("not json".Length);
    }

    [Fact]
    public async Task Logs_the_cli_and_exit_code_without_the_process_output_when_the_cli_exits_nonzero()
    {
        const string stdoutSecret = "STDOUT-PROSE-FROM-THE-MODEL-7";
        const string stderrSecret = "STDERR-DETAIL-FROM-THE-CLI-9";
        locator.Locate("copilot").Returns("/usr/bin/copilot");
        processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                CreateSession(SessionIdOf(call.Arg<ProcessSpec>()));
                return Task.FromResult(new ProcessResult(1, stdoutSecret, stderrSecret));
            });

        var failure = await Should.ThrowAsync<UnprocessableException>(() =>
            Analyzer().AnalyzeAsync(TwoRequests(), null, null, SessionSource.Copilot, SkillAnalysisScope.Session, TestContext.Current.CancellationToken));

        failure.Message.ShouldBe("The copilot CLI exited with code 1.");
        var entry = analyzerLog.Entries.Where(e => e.Level == LogLevel.Warning).ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Properties["Cli"].ShouldBe(SessionSource.Copilot);
        entry.Properties["ExitCode"].ShouldBe(1);
        analyzerLog.AllText().ShouldNotContain(text => text.Contains(stdoutSecret) || text.Contains(stderrSecret));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Keeps_the_session_when_cancelled_or_timed_out(bool callerCancels)
    {
        locator.Locate("copilot").Returns("/usr/bin/copilot");
        using var caller = new CancellationTokenSource();
        processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProcessResult>>(call =>
            {
                CreateSession(SessionIdOf(call.Arg<ProcessSpec>()));
                if (callerCancels)
                {
                    caller.Cancel();
                }

                throw new OperationCanceledException();
            });

        var analysis = Analyzer().AnalyzeAsync(TwoRequests(), null, null, SessionSource.Copilot, SkillAnalysisScope.Session, caller.Token);

        if (callerCancels)
        {
            await Should.ThrowAsync<OperationCanceledException>(() => analysis);
        }
        else
        {
            var failure = await Should.ThrowAsync<UnprocessableException>(() => analysis);
            failure.Message.ShouldBe("Skill analysis timed out after 2 minutes. Try a smaller date range.");
        }

        fs.Directory.GetDirectories($@"{CopilotHome}\session-state").Length.ShouldBe(1);
    }

    [Fact]
    public async Task Uses_a_fresh_session_id_per_run()
    {
        locator.Locate("copilot").Returns("/usr/bin/copilot");
        var ids = new List<string>();
        processes.RunAsync(Arg.Do<ProcessSpec>(spec => ids.Add(SessionIdOf(spec))), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ProcessResult(0, """{"opportunities":[]}""", string.Empty)));
        var analyzer = Analyzer();

        await analyzer.AnalyzeAsync(TwoRequests(), null, null, SessionSource.Copilot, SkillAnalysisScope.Session, TestContext.Current.CancellationToken);
        await analyzer.AnalyzeAsync(TwoRequests(), null, null, SessionSource.Copilot, SkillAnalysisScope.Session, TestContext.Current.CancellationToken);

        ids.Count.ShouldBe(2);
        ids[0].ShouldNotBe(ids[1]);
    }

    [Fact]
    public async Task Does_not_fail_the_analysis_when_cleanup_is_refused()
    {
        locator.Locate("copilot").Returns("/usr/bin/copilot");
        string? id = null;
        processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                id = SessionIdOf(call.Arg<ProcessSpec>());
                CreateSession(id, @"C:\other");
                return Task.FromResult(new ProcessResult(0, """{"opportunities":[]}""", string.Empty));
            });

        var result = await Analyzer().AnalyzeAsync(TwoRequests(), null, null, SessionSource.Copilot, SkillAnalysisScope.Session, TestContext.Current.CancellationToken);

        result.RequestsAnalyzed.ShouldBe(2);
        id.ShouldNotBeNull();
        fs.File.Exists($@"{SessionFolder(id)}\workspace.yaml").ShouldBeTrue();
    }

    [Fact]
    public async Task Does_not_fail_the_analysis_when_cleanup_fails()
    {
        locator.Locate("copilot").Returns("/usr/bin/copilot");
        processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ProcessResult(0, """{"opportunities":[]}""", string.Empty)));
        var failing = Substitute.For<IEvolveSessionCleanup>();
        failing.RemoveAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EvolveSessionCleanupOutcome.Failed));

        var result = await Analyzer(failing).AnalyzeAsync(TwoRequests(), null, null, SessionSource.Copilot, SkillAnalysisScope.Session, TestContext.Current.CancellationToken);

        result.RequestsAnalyzed.ShouldBe(2);
        await failing.Received(1).RemoveAsync(Arg.Any<string>(), false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Removes_the_session_inside_the_analysis_lease_after_the_cli_returned()
    {
        locator.Locate("copilot").Returns("/usr/bin/copilot");
        var events = new List<string>();
        processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                events.Add("run");
                return Task.FromResult(new ProcessResult(0, """{"opportunities":[]}""", string.Empty));
            });
        var recording = Substitute.For<IEvolveSessionCleanup>();
        recording.RemoveAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                events.Add($"cleanup:lease={activity.HasActiveAnalysis}");
                return Task.FromResult(EvolveSessionCleanupOutcome.Removed);
            });

        await Analyzer(recording).AnalyzeAsync(TwoRequests(), null, null, SessionSource.Copilot, SkillAnalysisScope.Session, TestContext.Current.CancellationToken);

        events.ShouldBe(["run", "cleanup:lease=True"]);
        activity.HasActiveAnalysis.ShouldBeFalse();
    }

    [Fact]
    public async Task Refuses_to_launch_when_the_session_id_already_exists()
    {
        locator.Locate("copilot").Returns("/usr/bin/copilot");
        var existing = Substitute.For<IEvolveSessionCleanup>();
        existing.Exists(Arg.Any<string>()).Returns(true);

        var failure = await Should.ThrowAsync<UnprocessableException>(() =>
            Analyzer(existing).AnalyzeAsync(TwoRequests(), null, null, SessionSource.Copilot, SkillAnalysisScope.Session, TestContext.Current.CancellationToken));

        failure.Message.ShouldBe("Could not start the analysis. Try again.");
        await processes.DidNotReceive().RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>());
        await existing.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Claude_runs_never_use_the_session_cleanup()
    {
        locator.Locate("claude").Returns("/usr/bin/claude");
        processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ProcessResult(0, """{"result":"{\"opportunities\":[]}"}""", string.Empty)));
        var untouched = Substitute.For<IEvolveSessionCleanup>();

        await Analyzer(untouched).AnalyzeAsync(TwoRequests(SessionSource.Claude), null, null, SessionSource.Claude, SkillAnalysisScope.Session, TestContext.Current.CancellationToken);

        untouched.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public void Argument_allow_list_rejects_a_trailing_newline()
    {
        SafeArgument.IsMatch("--silent").ShouldBeTrue();
        SafeArgument.IsMatch("--silent\n").ShouldBeFalse();
    }

    [Fact]
    public async Task Copilot_input_escapes_at_signs_without_changing_the_json_meaning()
    {
        locator.Locate("copilot").Returns("/usr/bin/copilot");
        ProcessSpec? captured = null;
        processes.RunAsync(Arg.Do<ProcessSpec>(spec => captured = spec), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ProcessResult(0, """{"opportunities":[]}""", string.Empty)));
        const string request = @"Summarize @C:\x\secret.txt and mail a@b.example";

        await AnalyzeAtRequests(SessionSource.Copilot, request);

        captured.ShouldNotBeNull();
        captured.StandardInput.ShouldNotBeNull();
        var data = captured.StandardInput[captured.StandardInput.IndexOf("[{\"id\"", StringComparison.Ordinal)..];
        data.ShouldNotContain("@");
        data.ShouldContain(@"\u0040C:\\x\\secret.txt");
        using var document = JsonDocument.Parse(data);
        document.RootElement[0].GetProperty("prompt").GetString().ShouldBe(request);
    }

    [Fact]
    public async Task Copilot_input_is_the_prompt_a_blank_line_and_the_escaped_json()
    {
        locator.Locate("copilot").Returns("/usr/bin/copilot");
        ProcessSpec? captured = null;
        processes.RunAsync(Arg.Do<ProcessSpec>(spec => captured = spec), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ProcessResult(0, """{"opportunities":[]}""", string.Empty)));

        await AnalyzeAtRequests(SessionSource.Copilot, "Summarize @x");

        const string lastPromptLine = "Analyze the JSON requests provided on standard input.";
        captured.ShouldNotBeNull();
        captured.StandardInput.ShouldNotBeNull();
        var promptEnd = captured.StandardInput.IndexOf(lastPromptLine, StringComparison.Ordinal) + lastPromptLine.Length;
        var prompt = captured.StandardInput[..promptEnd];
        prompt.ShouldStartWith("Find repeated task patterns");
        const string escapedAt = "\\" + "u0040";
        const string data = $$"""[{"id":"R001","prompt":"Summarize {{escapedAt}}x"},{"id":"R002","prompt":"Summarize {{escapedAt}}x"}]""";
        captured.StandardInput.ShouldBe(prompt + "\n\n" + data);
    }

    [Fact]
    public async Task Claude_input_keeps_at_signs_and_stays_the_bare_json()
    {
        locator.Locate("claude").Returns("/usr/bin/claude");
        ProcessSpec? captured = null;
        processes.RunAsync(Arg.Do<ProcessSpec>(spec => captured = spec), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ProcessResult(0, """{"result":"{\"opportunities\":[]}"}""", string.Empty)));

        await AnalyzeAtRequests(SessionSource.Claude, @"Summarize @C:\x\secret.txt");

        captured.ShouldNotBeNull();
        captured.StandardInput.ShouldBe("""[{"id":"R001","prompt":"Summarize @C:\\x\\secret.txt"},{"id":"R002","prompt":"Summarize @C:\\x\\secret.txt"}]""");
    }

    private async Task AnalyzeAtRequests(SessionSource cli, string request)
    {
        var session = new Session
        {
            Id = "session-a",
            Source = cli,
            SkillPrompts = [new SkillPrompt(request, Start), new SkillPrompt(request, Start.AddMinutes(1))],
        };
        var analyzer = Analyzer();

        await analyzer.AnalyzeAsync([session], null, null, cli, SkillAnalysisScope.Session, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_not_run_a_model_for_fewer_than_two_qualifying_requests()
    {
        var session = new Session
        {
            Id = "session-a",
            Source = SessionSource.Claude,
            SkillPrompts = [new SkillPrompt("A single repeated workflow request", null)],
        };
        var analyzer = Analyzer();

        var result = await analyzer.AnalyzeAsync([session], null, null, SessionSource.Claude, SkillAnalysisScope.Session,
            TestContext.Current.CancellationToken);

        result.RequestsAnalyzed.ShouldBe(1);
        result.Opportunities.ShouldBeEmpty();
        await processes.DidNotReceive().RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Uses_claude_without_tools_or_session_persistence()
    {
        locator.Locate("claude").Returns("/usr/bin/claude");
        processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ProcessResult(0,
                """{"type":"result","result":"{\"opportunities\":[{\"title\":\"Build checks\",\"description\":\"Repeated build requests.\",\"instructions\":\"# Build checks\",\"requestIds\":[\"R001\",\"R002\"]}]}"}""",
                string.Empty)));
        var session = new Session
        {
            Id = "session-a",
            Source = SessionSource.Claude,
            SkillPrompts =
            [
                new SkillPrompt("Run the build and report compilation errors", Start),
                new SkillPrompt("Build this project and summarize failures", Start.AddMinutes(1)),
            ],
        };
        options.Value.ClaudeConfigDir = "/tmp/claude-config";
        var analyzer = Analyzer();

        var result = await analyzer.AnalyzeAsync([session], null, null, SessionSource.Claude, SkillAnalysisScope.Session,
            TestContext.Current.CancellationToken);

        result.Opportunities.Single().Title.ShouldBe("Build checks");
        await processes.Received(1).RunAsync(
            Arg.Is<ProcessSpec>(spec =>
                spec.FileName == "/usr/bin/claude"
                && spec.Arguments.Contains("--tools")
                && spec.Arguments.Contains("--strict-mcp-config")
                && spec.Arguments.Contains("--no-session-persistence")
                && spec.Arguments.Contains("--settings")
                && spec.Arguments.Contains("""{"disableAllHooks":true}""")
                && spec.Environment != null
                && spec.Environment["CLAUDE_CONFIG_DIR"] == options.Value.ClaudeConfigDir
                && spec.Environment.Count == 1
                && spec.RemoveEnvironment == null
                && !spec.Arguments.Contains("--session-id")
                && spec.StandardInput != null
                && !spec.Arguments.Any(argument => argument.Contains("compilation errors"))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Analyzes_short_repeated_requests()
    {
        locator.Locate("copilot").Returns("/usr/bin/copilot");
        processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ProcessResult(0, """{"opportunities":[]}""", string.Empty)));
        var session = new Session
        {
            Id = "session-a",
            Source = SessionSource.Copilot,
            SkillPrompts =
            [
                new SkillPrompt("Run tests", Start),
                new SkillPrompt("Rerun tests", Start.AddMinutes(1)),
            ],
        };
        var analyzer = Analyzer();

        var result = await analyzer.AnalyzeAsync([session], null, null, SessionSource.Copilot, SkillAnalysisScope.Session,
            TestContext.Current.CancellationToken);

        result.RequestsAnalyzed.ShouldBe(2);
        await processes.Received(1).RunAsync(
            Arg.Is<ProcessSpec>(spec => spec.StandardInput!.Contains("Run tests") && spec.StandardInput.Contains("Rerun tests")),
            Arg.Any<CancellationToken>());
    }
}
