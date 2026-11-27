using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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
        var analyzer = new SkillAnalyzer(locator, processes, activity, Options());

        var result = await analyzer.AnalyzeAsync([first, second], Start.AddDays(1), null, SessionSource.Copilot,
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
        captured.Arguments.ShouldBe(
        [
            "--silent", "--no-custom-instructions", "--disable-builtin-mcps",
            "--disable-mcp-server", "computer-use", "--no-ask-user",
            "--deny-tool", "shell", "--deny-tool", "write", "--deny-tool", "read",
            "--deny-tool", "url", "--deny-tool", "memory",
        ]);
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
        var analyzer = new SkillAnalyzer(locator, processes, activity, Options());

        await analyzer.AnalyzeAsync([session], null, null, cli, TestContext.Current.CancellationToken);
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
        var analyzer = new SkillAnalyzer(locator, processes, activity, Options());

        var result = await analyzer.AnalyzeAsync([session], null, null, SessionSource.Claude,
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
        var options = Options();
        options.Value.ClaudeConfigDir = "/tmp/claude-config";
        var analyzer = new SkillAnalyzer(locator, processes, activity, options);

        var result = await analyzer.AnalyzeAsync([session], null, null, SessionSource.Claude,
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
        var analyzer = new SkillAnalyzer(locator, processes, activity, Options());

        var result = await analyzer.AnalyzeAsync([session], null, null, SessionSource.Copilot,
            TestContext.Current.CancellationToken);

        result.RequestsAnalyzed.ShouldBe(2);
        await processes.Received(1).RunAsync(
            Arg.Is<ProcessSpec>(spec => spec.StandardInput!.Contains("Run tests") && spec.StandardInput.Contains("Rerun tests")),
            Arg.Any<CancellationToken>());
    }
}
