using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using SessionInsights.Application.Common;
using SessionInsights.Domain.Reports;
using SessionInsights.Domain.Sessions;
using SessionInsights.Infrastructure.Sessions;
using Xunit;
using static SessionInsights.Infrastructure.Tests.TestSupport;

namespace SessionInsights.Infrastructure.Tests;

public sealed class HarnessReaderTests
{
    private readonly MockFileSystem fs = new();
    private readonly string root = Path.Combine(Path.GetTempPath(), "harness-tests");

    private IOptions<SessionInsightsOptions> Settings => Microsoft.Extensions.Options.Options.Create(new SessionInsightsOptions
    {
        ClaudeRoot = Path.Combine(root, "claude"),
        CopilotRoot = Path.Combine(root, "copilot"),
    });

    private static string Activation(int minute, string? version, string reason = "startup") =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "attachment",
            timestamp = Ts(minute),
            version = "2.1.302",
            attachment = new
            {
                type = "hook_success",
                hookEvent = "SessionStart",
                hookName = $"SessionStart:{reason}",
                stdout = version is null ? "no version" : $@"C:\plugins\msdc-harness\{version}\scripts\start",
                durationMs = 0,
            },
        });

    private static string ToolStart(int minute, string id) =>
        $$$"""{"type":"assistant","timestamp":"{{{Ts(minute)}}}","message":{"content":[{"type":"tool_use","id":"{{{id}}}","name":"Bash"}]}}""";

    private static string ToolEnd(int minute, string id, bool failed = false) =>
        $$$"""{"type":"user","timestamp":"{{{Ts(minute)}}}","message":{"content":[{"type":"tool_result","tool_use_id":"{{{id}}}","is_error":{{{failed.ToString().ToLowerInvariant()}}}}]}}""";

    private static string FailedHook(int minute, long? duration)
    {
        var attachment = new Dictionary<string, object?>
        {
            ["type"] = "hook_blocking_error",
            ["hookEvent"] = "PreToolUse",
        };
        if (duration is { } milliseconds)
        {
            attachment["durationMs"] = milliseconds;
        }

        return System.Text.Json.JsonSerializer.Serialize(new { type = "attachment", timestamp = Ts(minute), attachment });
    }

    private Session Claude(params string[] lines)
    {
        var id = NewId();
        WriteLines(fs, Path.Combine(Settings.Value.ClaudeRoot!, "project", $"{id}.jsonl"), lines);
        var reader = new ClaudeSessionReader(fs, Settings, NullLogger<ClaudeSessionReader>.Instance);
        return reader.Parse(reader.Discover().Single(), Now)!;
    }

    [Fact]
    public void Claude_splits_resumed_versions_and_does_not_assign_unknown_or_cross_boundary_activity()
    {
        var session = Claude(
            ToolStart(0, "before"), ToolEnd(1, "before"),
            Activation(2, "0.26.0"), ToolStart(3, "a"), ToolEnd(4, "a", true),
            ToolStart(5, "crossing"), Activation(6, "0.26.1", "resume"), ToolEnd(7, "crossing"),
            ToolStart(8, "b"), ToolEnd(9, "b"),
            Activation(10, null, "resume"), ToolStart(11, "unknown"), ToolEnd(12, "unknown"));

        session.Harness.Observations.Select(o => o.Version).ShouldBe(["0.26.0", "0.26.1", null]);
        session.Harness.Activity.Single(a => a.Version == "0.26.0").ToolFailures.ShouldBe(1);
        session.Harness.Activity.Single(a => a.Version == "0.26.1").ToolCalls.ShouldBe(1);
        session.Harness.Activity.Single(a => a.Version is null).ToolCalls.ShouldBe(3);
        session.ToolCallTotal.ShouldBe(5);
        session.Harness.Observations.ShouldAllBe(o => o.CliVersion == "2.1.302");
    }

    [Fact]
    public void Claude_conflicting_paths_and_untimed_activations_remain_unknown()
    {
        var first = Activation(0, "0.26.0").Replace(
            @"0.26.0", @"0.26.0\\scripts C:\\plugins\\msdc-harness\\0.26.1", StringComparison.Ordinal);
        var session = Claude(first, ToolStart(1, "a"), ToolEnd(2, "a"));

        session.Harness.Observations.Single().Evidence.ShouldBe("conflicting");
        session.Harness.Activity.ShouldAllBe(a => a.Version == null);

        foreach (var file in fs.AllFiles.ToArray())
        {
            fs.RemoveFile(file);
        }
        var untimed = Activation(0, "0.26.0").Replace($"\"timestamp\":\"{Ts(0)}\",", "", StringComparison.Ordinal);
        Claude(untimed, ToolStart(1, "a"), ToolEnd(2, "a")).Harness.Activity.ShouldAllBe(a => a.Version == null);
    }

    [Fact]
    public void Claude_hooks_with_missing_or_invalid_durations_remain_unattributed_and_counted()
    {
        var session = Claude(
            Activation(0, "0.26.0"),
            FailedHook(1, null),
            FailedHook(2, -1));

        session.Harness.Activity.ShouldAllBe(activity => activity.Version == null);
        session.HookTotal.ShouldBe(3);
        session.HookFailed.ShouldBe(2);
        session.HookTypes.Single(hook => hook.Type == "PreToolUse").Count.ShouldBe(2);
        session.HookTypes.Single(hook => hook.Type == "PreToolUse").Failed.ShouldBe(2);
    }

    [Fact]
    public void Claude_does_not_assign_subagent_health_to_parent_activation()
    {
        var id = NewId();
        WriteLines(fs, Path.Combine(Settings.Value.ClaudeRoot!, "project", $"{id}.jsonl"),
            [Activation(0, "0.26.0"), ToolStart(1, "a"), ToolEnd(2, "a")]);
        WriteLines(fs, Path.Combine(Settings.Value.ClaudeRoot!, "project", id, "subagents", "agent-1.jsonl"),
            [ToolStart(3, "child"), ToolEnd(4, "child", true)]);
        var reader = new ClaudeSessionReader(fs, Settings, NullLogger<ClaudeSessionReader>.Instance);
        var session = reader.Parse(reader.Discover().Single(), Now)!;

        session.Harness.Activity.Single(a => a.Version is null).ToolFailures.ShouldBe(1);
        session.Harness.Activity.Single(a => a.Version == "0.26.0").ToolFailures.ShouldBe(0);
    }

    [Fact]
    public void Copilot_keeps_per_invocation_versions_missing_values_and_excludes_reports()
    {
        var id = NewId();
        WriteLines(fs, Path.Combine(Settings.Value.CopilotRoot!, id, "events.jsonl"),
        [
            Json("session.start", Ts(0), """{"copilotVersion":"1.0.105-build.123"}"""),
            Json("skill.invoked", Ts(1), """{"name":"test","pluginName":"msdc-harness","pluginVersion":"0.24.0"}"""),
            Json("skill.invoked", Ts(2), """{"name":"test","pluginName":"msdc-harness","pluginVersion":null}"""),
            Json("skill.invoked", Ts(3), """{"name":"other","pluginName":"unrelated","pluginVersion":"9.9.9"}"""),
            Json("tool.execution_start", Ts(4), """{"toolCallId":"a","toolName":"view"}"""),
            Json("tool.execution_complete", Ts(5), """{"toolCallId":"a","success":false}"""),
            Json("user.message", Ts(6), $$"""{"content":"{{ReportConstants.Prompt}}"}"""),
            Json("skill.invoked", Ts(7), """{"name":"report","pluginName":"msdc-harness","pluginVersion":"0.25.0"}"""),
        ]);
        var reader = new CopilotSessionReader(fs, Settings, NullLogger<CopilotSessionReader>.Instance);
        var session = reader.Parse(reader.Discover().Single(), Now)!;

        session.Harness.Observations.Select(o => o.Version).ShouldBe(["0.24.0", null]);
        session.Harness.Observations.ShouldAllBe(o => o.Kind == "invocation" && o.CliVersion == "1.0.105-build.123");
        session.Harness.Activity.Single().Version.ShouldBeNull();
        session.Harness.Activity.Single().ToolFailures.ShouldBe(1);
    }
}
