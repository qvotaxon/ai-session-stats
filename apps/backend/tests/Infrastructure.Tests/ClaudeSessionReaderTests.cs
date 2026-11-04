using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SessionInsights.Domain.Reports;
using SessionInsights.Infrastructure.Sessions;
using Xunit;
using static SessionInsights.Infrastructure.Tests.TestSupport;

namespace SessionInsights.Infrastructure.Tests;

public sealed class ClaudeSessionReaderTests
{
    private readonly MockFileSystem fs = new();

    private ClaudeSessionReader Reader() => new(fs, Options(), NullLogger<ClaudeSessionReader>.Instance);

    private static string User(int minute, string text, string kind = "human", string extra = "") =>
        $$$"""{"type":"user","timestamp":"{{{Ts(minute)}}}","cwd":"C:\\work\\demo","origin":{"kind":"{{{kind}}}"}{{{extra}}},"message":{"role":"user","content":"{{{text}}}"}}""";

    private static string Assistant(int minute, string msgId, string tools = "", int output = 10) =>
        $$$"""{"type":"assistant","timestamp":"{{{Ts(minute)}}}","message":{"id":"{{{msgId}}}","model":"claude-x","usage":{"input_tokens":5,"output_tokens":{{{output}}},"cache_read_input_tokens":100,"cache_creation_input_tokens":20},"content":[{{{tools}}}]}}""";

    private SessionFileGroup Write(string id, params string[] lines)
    {
        WriteLines(fs, $@"{ClaudeRoot}\proj\{id}.jsonl", lines);
        return Reader().Discover().Single(g => g.SessionId == id);
    }

    [Fact]
    public void Parse_counts_tokens_once_per_message_and_tools_hooks_guards()
    {
        var id = NewId();
        var group = Write(
            id,
            User(0, "hello"),
            Assistant(1, "m1", """{"type":"tool_use","id":"t1","name":"Bash"}"""),
            Assistant(1, "m1", """{"type":"tool_use","id":"t2","name":"Read"}"""),
            """{"type":"user","timestamp":"2026-01-10T10:02:00.000Z","message":{"content":[{"type":"tool_result","tool_use_id":"t1","is_error":true}]}}""",
            """{"type":"user","timestamp":"2026-01-10T10:02:01.000Z","message":{"content":[{"type":"tool_result","tool_use_id":"t2","is_error":false}]}}""",
            $$$"""{"type":"attachment","timestamp":"{{{Ts(3)}}}","attachment":{"type":"hook_success","hookEvent":"Stop","hookName":"Stop","exitCode":"0","durationMs":12}}""",
            $$$"""{"type":"attachment","timestamp":"{{{Ts(3, 5)}}}","attachment":{"type":"hook_blocking_error","hookEvent":"PreToolUse","hookName":"PreToolUse:Bash"}}""",
            $$"""{"type":"ai-title","aiTitle":"Nice title"}""");

        var session = Reader().Parse(group, Now)!;

        session.Tokens.Output.ShouldBe(10, "assistant lines sharing a message id are one API response");
        session.Tokens.Total.ShouldBe(135);
        session.ToolCallsSucceeded.ShouldBe(1);
        session.ToolCallsFailed.ShouldBe(1);
        session.HookTotal.ShouldBe(2);
        session.GuardTotal.ShouldBe(1);
        session.Title.ShouldBe("Nice title");
        session.Project.ShouldBe("demo");
        session.Cwd.ShouldBe(@"C:\work\demo");
        session.LooksLikeReportFork.ShouldBeFalse();
    }

    [Fact]
    public void Parse_aggregates_subagent_files_into_the_parent()
    {
        var id = NewId();
        var group = Write(id, User(0, "hello"), Assistant(1, "m1"));
        WriteLines(fs, $@"{ClaudeRoot}\proj\{id}\subagents\agent-1.jsonl", [Assistant(2, "s1", output: 90)]);
        group = Reader().Discover().Single(g => g.SessionId == id);

        var session = Reader().Parse(group, Now)!;

        session.SubagentTotal.ShouldBe(1);
        session.Tokens.Output.ShouldBe(100);
        Reader().Discover().Count.ShouldBe(1, "subagent files are never listed as sessions of their own");
    }

    [Fact]
    public void Parse_flags_a_report_fork_by_first_human_message()
    {
        var id = NewId();
        var group = Write(id, User(0, ReportConstants.Prompt), Assistant(1, "m1"));

        Reader().Parse(group, Now)!.LooksLikeReportFork.ShouldBeTrue();
    }

    [Fact]
    public void Parse_does_not_flag_a_normal_session_that_asked_for_a_report_later()
    {
        var id = NewId();
        var group = Write(id, User(0, "work"), Assistant(1, "m1"), User(2, ReportConstants.Prompt), Assistant(3, "m2"));

        Reader().Parse(group, Now)!.LooksLikeReportFork.ShouldBeFalse();
    }

    [Fact]
    public void Parse_survives_malformed_lines()
    {
        var id = NewId();
        var group = Write(id, User(0, "hello"), "{broken", """{"type":"assistant","message":{"id":""", Assistant(1, "m1"));

        var session = Reader().Parse(group, Now);

        session.ShouldNotBeNull();
        session.Tokens.Output.ShouldBe(10);
    }
}
