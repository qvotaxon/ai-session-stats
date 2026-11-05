using System;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SessionInsights.Domain.Reports;
using SessionInsights.Infrastructure.Sessions;
using Xunit;
using static SessionInsights.Infrastructure.Tests.TestSupport;

namespace SessionInsights.Infrastructure.Tests;

public sealed class CopilotSessionReaderTests
{
    private readonly MockFileSystem fs = new();

    private CopilotSessionReader Reader() => new(fs, Options(), NullLogger<CopilotSessionReader>.Instance);

    private SessionFileGroup Write(string id, params string[] lines)
    {
        WriteLines(fs, $@"{CopilotRoot}\{id}\events.jsonl", lines);
        fs.AddFile($@"{CopilotRoot}\{id}\workspace.yaml", new MockFileData("id: x\ncwd: C:\\work\\demo\nname: Demo session\ncreated_at: 2026-01-10T10:00:00Z\n"));
        return Reader().Discover().Single(g => g.SessionId == id);
    }

    [Fact]
    public void Parse_uses_the_overview_of_the_latest_compaction_as_recap()
    {
        var id = NewId();
        var group = Write(
            id,
            Json("session.start", Ts(0), "{}"),
            Json("user.message", Ts(1), """{"content":"hello"}"""),
            Json("session.compaction_complete", Ts(2), """{"summaryContent":"<overview>first</overview>"}"""),
            Json("session.compaction_complete", Ts(5), """{"summaryContent":"<history>x</history><overview>\n second \n</overview>"}"""));

        var session = Reader().Parse(group, Now)!;

        session.Recap.ShouldBe("second");
        session.RecapAt.ShouldBe(DateTimeOffset.Parse(Ts(5)));
    }

    [Fact]
    public void Parse_ignores_compactions_without_an_overview_section()
    {
        var id = NewId();
        var group = Write(
            id,
            Json("session.start", Ts(0), "{}"),
            Json("user.message", Ts(1), """{"content":"hello"}"""),
            Json("session.compaction_complete", Ts(2), """{"summaryContent":"<overview>kept</overview>"}"""),
            Json("session.compaction_complete", Ts(3), """{"summaryContent":"<history>no overview</history>"}"""),
            Json("session.compaction_complete", Ts(4), """{"summaryContent":"<overview>   </overview>"}"""),
            Json("session.compaction_complete", Ts(5), "{}"));

        var session = Reader().Parse(group, Now)!;

        session.Recap.ShouldBe("kept");
        session.RecapAt.ShouldBe(DateTimeOffset.Parse(Ts(2)));
    }

    [Fact]
    public void Parse_has_no_recap_when_no_compaction_happened()
    {
        var group = Write(NewId(), Json("session.start", Ts(0), "{}"), Json("user.message", Ts(1), """{"content":"hello"}"""));

        var session = Reader().Parse(group, Now)!;

        session.Recap.ShouldBeNull();
        session.RecapAt.ShouldBeNull();
    }

    [Fact]
    public void Parse_truncates_an_overlong_recap()
    {
        var overview = new string('x', 5000);
        var group = Write(
            NewId(),
            Json("session.start", Ts(0), "{}"),
            Json("user.message", Ts(1), """{"content":"hello"}"""),
            Json("session.compaction_complete", Ts(2), $$"""{"summaryContent":"<overview>{{overview}}</overview>"}"""));

        Reader().Parse(group, Now)!.Recap!.Length.ShouldBe(SessionInsights.Domain.Sessions.Session.MaxRecapLength);
    }

    [Fact]
    public void Parse_with_shutdown_uses_model_metrics_and_normalises_cache_tokens()
    {
        var id = NewId();
        var group = Write(
            id,
            Json("session.start", Ts(0), "{}"),
            Json("user.message", Ts(1), """{"content":"hello"}"""),
            Json("assistant.turn_start", Ts(1, 5), "{}"),
            Json("tool.execution_start", Ts(2), """{"toolCallId":"a","toolName":"view"}"""),
            Json("tool.execution_complete", Ts(2, 1), """{"toolCallId":"a","success":true}"""),
            Json("tool.execution_start", Ts(3), """{"toolCallId":"b","toolName":"powershell"}"""),
            Json("tool.execution_complete", Ts(3, 2), """{"toolCallId":"b","success":false,"error":{"code":"denied"}}"""),
            Json("hook.start", Ts(4), """{"hookInvocationId":"h1","hookType":"preToolUse"}"""),
            Json("hook.end", Ts(4, 1), """{"hookInvocationId":"h1","hookType":"preToolUse","success":true}"""),
            Json("subagent.started", Ts(5), """{"agentName":"explore"}"""),
            Json("session.shutdown", Ts(9), """{"totalPremiumRequests":2,"totalApiDurationMs":1500,"modelMetrics":{"m1":{"requests":{"count":3,"cost":2},"usage":{"inputTokens":1000,"outputTokens":50,"cacheReadTokens":600,"cacheWriteTokens":100,"reasoningTokens":10}}},"codeChanges":{"linesAdded":7,"linesRemoved":2,"filesModified":["a","b"]}}"""));

        var session = Reader().Parse(group, Now)!;

        session.Source.ToString().ShouldBe("Copilot");
        session.Title.ShouldBe("Demo session");
        session.Project.ShouldBe("demo");
        session.Tokens.Input.ShouldBe(300);
        session.Tokens.CacheRead.ShouldBe(600);
        session.Tokens.Total.ShouldBe(1050);
        session.ToolCallsSucceeded.ShouldBe(1);
        session.ToolCallsFailed.ShouldBe(1);
        session.GuardTotal.ShouldBe(1, "the denied tool call is a permission denial");
        session.HookTotal.ShouldBe(1);
        session.SubagentTotal.ShouldBe(1);
        session.CodeChanges!.FilesModified.ShouldBe(2);
        session.UserTurns.ShouldBe(1);
    }

    [Fact]
    public void Parse_without_shutdown_sums_usage_records()
    {
        var id = NewId();
        var group = Write(
            id,
            Json("session.start", Ts(0), "{}"),
            Json("user.message", Ts(1), """{"content":"hi"}"""),
            Json("session.usage_record", Ts(2), """{"usage":{"model":"m1","inputTokens":100,"outputTokens":10,"cacheReadTokens":40,"cacheWriteTokens":0}}"""),
            Json("session.usage_record", Ts(3), """{"usage":{"model":"m1","inputTokens":200,"outputTokens":20,"cacheReadTokens":0,"cacheWriteTokens":0}}"""));

        var session = Reader().Parse(group, Now)!;

        session.Tokens.Output.ShouldBe(30);
        session.Tokens.Total.ShouldBe(330);
        session.Models.ShouldBe(["m1"]);
    }

    [Fact]
    public void Parse_excludes_the_report_turn_from_metrics()
    {
        var id = NewId();
        var group = Write(
            id,
            Json("session.start", Ts(0), "{}"),
            Json("user.message", Ts(1), """{"content":"real work"}"""),
            Json("tool.execution_start", Ts(2), """{"toolCallId":"a","toolName":"view"}"""),
            Json("tool.execution_complete", Ts(2, 1), """{"toolCallId":"a","success":true}"""),
            Json("session.usage_record", Ts(3), """{"usage":{"model":"m1","inputTokens":100,"outputTokens":10}}"""),
            Json("user.message", Ts(4), $$"""{"content":"{{ReportConstants.Prompt}}"}"""),
            Json("tool.execution_start", Ts(5), """{"toolCallId":"b","toolName":"view"}"""),
            Json("tool.execution_complete", Ts(5, 1), """{"toolCallId":"b","success":true}"""),
            Json("session.usage_record", Ts(6), """{"usage":{"model":"m1","inputTokens":9000,"outputTokens":900}}"""));

        var session = Reader().Parse(group, Now)!;

        session.ToolCallTotal.ShouldBe(1);
        session.UserTurns.ShouldBe(1);
        session.Tokens.Total.ShouldBe(110);
    }

    [Fact]
    public void Parse_survives_malformed_and_truncated_lines()
    {
        var id = NewId();
        var group = Write(
            id,
            Json("session.start", Ts(0), "{}"),
            "{ this is not json",
            Json("user.message", Ts(1), """{"content":"hi"}"""),
            """{"type":"user.message","data":{"con""");

        var session = Reader().Parse(group, Now);

        session.ShouldNotBeNull();
        session.UserTurns.ShouldBe(1);
    }

    [Fact]
    public void Parse_treats_repeated_shutdowns_as_cumulative_not_additive()
    {
        var id = NewId();
        const string Metrics = "\"modelMetrics\":{\"m1\":{\"requests\":{\"count\":3,\"cost\":2},\"usage\":{\"inputTokens\":1000,\"outputTokens\":50,\"cacheReadTokens\":600,\"cacheWriteTokens\":100}}}";
        var group = Write(
            id,
            Json("session.start", Ts(0), "{}"),
            Json("session.shutdown", Ts(5), "{\"totalPremiumRequests\":2,\"totalNanoAiu\":1000,\"totalApiDurationMs\":1500," + Metrics + "}"),
            Json("session.shutdown", Ts(6), "{\"totalPremiumRequests\":2,\"totalNanoAiu\":1000,\"totalApiDurationMs\":1500," + Metrics + "}"),
            Json("session.shutdown", Ts(9), "{\"totalPremiumRequests\":5,\"totalNanoAiu\":3000,\"totalApiDurationMs\":4000,\"modelMetrics\":{\"m1\":{\"requests\":{\"count\":7,\"cost\":5},\"usage\":{\"inputTokens\":2000,\"outputTokens\":80,\"cacheReadTokens\":600,\"cacheWriteTokens\":100}}}}"));

        var session = Reader().Parse(group, Now)!;

        session.Cost!.PremiumRequests.ShouldBe(5);
        session.Cost.NanoAiu.ShouldBe(3000);
        session.ApiDurationMs.ShouldBe(4000);
        session.ApiRequestCount.ShouldBe(7);
        session.Tokens.Output.ShouldBe(80);
        session.Tokens.Total.ShouldBe(2000 - 700 + 80 + 700);
    }

    [Fact]
    public void Discover_ignores_non_guid_folders()
    {
        fs.AddFile($@"{CopilotRoot}\.session-operation-locks\events.jsonl", new MockFileData("{}"));
        Reader().Discover().ShouldBeEmpty();
    }
}
