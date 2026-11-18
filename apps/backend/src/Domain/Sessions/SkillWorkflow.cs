using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SessionInsights.Domain.Sessions;

public sealed record SkillSignal(string Workflow, DateTimeOffset? At);

public sealed record SkillWorkflow(string Id, string Title, string Outline, Regex Pattern)
{
    public static IReadOnlyList<SkillWorkflow> Known { get; } =
    [
        Define("run-tests", "Run and summarize tests",
            "Capture how to select the relevant suite, run the project's test command, and summarize failures.",
            @"(?:run|rerun|execute)\b.{0,100}\btests?\b"),
        Define("write-tests", "Write tests",
            "Document the test framework, fixture conventions, and the behavior and edge cases to cover.",
            @"(?:add|write|create|generate|update)\b.{0,100}\b(?:tests?|test coverage)\b"),
        Define("fix-tests", "Diagnose failing tests",
            "Capture how to reproduce a failure, isolate its cause, and verify the fix with targeted tests.",
            @"(?:fix|debug|diagnose|investigate|troubleshoot)\b.{0,100}\b(?:tests?|test failures?)\b"),
        Define("review-code", "Review code changes",
            "Describe what diff to inspect and how to report actionable findings with file locations.",
            @"(?:review|audit)\b.{0,100}\b(?:changes?|diff|code|pr|pull request|patch)\b|code review\b"),
        Define("update-docs", "Update documentation",
            "List which docs to update, how to verify examples, and the project's writing conventions.",
            @"(?:update|write|add|generate|revise)\b.{0,100}\b(?:docs|documentation|readme|changelog|release notes)\b"),
        Define("build-project", "Build the project",
            "Specify the build command, required setup, and what to report when the build fails.",
            @"(?:build|compile)(?:\s+(?:(?:the|this)\s+)?(?:project|app|repo|repository|solution|codebase)\b|$)|(?:run|rerun)\b.{0,100}\bbuild\b"),
        Define("lint-code", "Run lint and formatting checks",
            "Specify the lint and formatting commands, where to run them, and when to apply fixes.",
            @"(?:lint|format)\b|(?:run|rerun|fix)\b.{0,100}\b(?:lint|linter|formatting)\b"),
        Define("refactor-code", "Refactor code safely",
            "Capture the project's refactoring conventions, what behavior to preserve, and how to verify it.",
            @"refactor\b"),
    ];

    public static SkillSignal? Detect(string? prompt, DateTimeOffset? at)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return null;
        }

        var lead = prompt.AsSpan().TrimStart();
        lead = lead[..Math.Min(lead.Length, 200)];
        var lineEnd = lead.IndexOfAny('\r', '\n');
        var firstLine = (lineEnd < 0 ? lead : lead[..lineEnd]).TrimEnd().ToString();
        foreach (var workflow in Known)
        {
            if (workflow.Pattern.IsMatch(firstLine))
            {
                return new SkillSignal(workflow.Id, at);
            }
        }

        return null;
    }

    private static SkillWorkflow Define(string id, string title, string outline, string pattern) =>
        new(id, title, outline, new Regex(
            @"^(?:(?:please|(?:can|could|would) you|let's)\s+)*(?:" + pattern + ")",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking));
}
