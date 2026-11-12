using System;
using System.Collections.Generic;
using System.Linq;
using SessionInsights.Domain.Sessions;

namespace SessionInsights.Infrastructure.Sessions;

/// <summary>Collects counters while a parser walks a session and builds the final <see cref="Session"/>. Metrics only.</summary>
internal sealed class SessionAccumulator
{
    private const int MaxBuckets = 60;

    private enum EventKind { Tool, Hook, Guard, Tokens }

    private readonly record struct TimelineEvent(long Ticks, EventKind Kind, long Amount);

    private readonly Dictionary<string, (int Count, int Failed, long Duration)> tools = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Count, int Failed, long Duration)> hooks = new(StringComparer.Ordinal);
    private readonly Dictionary<(GuardKind, string), int> guards = [];
    private readonly Dictionary<string, int> skills = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> subagents = new(StringComparer.Ordinal);
    private readonly List<TimelineEvent> timeline = [];
    private readonly List<(DateTimeOffset? At, DateTimeOffset? StartedAt, bool Tool, bool Failed, bool Eligible)> health = [];
    public List<HarnessObservation> HarnessObservations { get; } = [];

    public DateTimeOffset? First { get; private set; }
    public DateTimeOffset? Last { get; private set; }
    public int ToolsSucceeded { get; private set; }
    public int ToolsFailed { get; private set; }
    public int HookTotal { get; private set; }
    public int HookFailed { get; private set; }
    public int UserTurns { get; set; }
    public int AssistantTurns { get; set; }
    public int SubagentCount { get; set; }

    public void Seen(DateTimeOffset? t)
    {
        if (t is null)
        {
            return;
        }

        if (First is null || t < First)
        {
            First = t;
        }

        if (Last is null || t > Last)
        {
            Last = t;
        }
    }

    public void Tool(string name, bool succeeded, long durationMs, DateTimeOffset? at, bool harnessEligible = true, DateTimeOffset? startedAt = null)
    {
        health.Add((at, startedAt, true, !succeeded, harnessEligible));
        tools.TryGetValue(name, out var cur);
        tools[name] = (cur.Count + 1, cur.Failed + (succeeded ? 0 : 1), cur.Duration + Math.Max(0, durationMs));
        if (succeeded)
        {
            ToolsSucceeded++;
        }
        else
        {
            ToolsFailed++;
        }

        Mark(at, EventKind.Tool, 1);
    }

    public void Hook(string type, bool failed, long? durationMs, DateTimeOffset? at, bool harnessEligible = true)
    {
        DateTimeOffset? startedAt = null;
        if (at is { } time && durationMs is { } duration && duration >= 0 && duration <= (time - DateTimeOffset.MinValue).TotalMilliseconds)
        {
            startedAt = time - TimeSpan.FromMilliseconds(duration);
        }

        health.Add((at, startedAt, false, failed, harnessEligible));
        hooks.TryGetValue(type, out var cur);
        hooks[type] = (cur.Count + 1, cur.Failed + (failed ? 1 : 0), cur.Duration + Math.Max(0, durationMs ?? 0));
        HookTotal++;
        if (failed)
        {
            HookFailed++;
        }

        Mark(at, EventKind.Hook, 1);
    }

    public void Guard(GuardKind kind, string name, DateTimeOffset? at)
    {
        guards.TryGetValue((kind, name), out var n);
        guards[(kind, name)] = n + 1;
        Mark(at, EventKind.Guard, 1);
    }

    public void Skill(string name) => skills[name] = skills.GetValueOrDefault(name) + 1;

    public void Subagent(string name)
    {
        subagents[name] = subagents.GetValueOrDefault(name) + 1;
        SubagentCount++;
    }

    public void TokenEvent(DateTimeOffset? at, long tokens) => Mark(at, EventKind.Tokens, tokens);

    public HarnessInfo BuildHarness() => new(
        [.. HarnessObservations.OrderBy(o => o.At)],
        HarnessEvidence.Attribute(HarnessObservations, health));

    private void Mark(DateTimeOffset? at, EventKind kind, long amount)
    {
        Seen(at);
        if (at is { } t)
        {
            timeline.Add(new TimelineEvent(t.UtcTicks, kind, amount));
        }
    }

    public IReadOnlyList<ToolStat> ToolStats() =>
        [.. tools.Select(t => new ToolStat(t.Key, t.Value.Count, t.Value.Failed, t.Value.Duration)).OrderByDescending(t => t.Count).ThenBy(t => t.Name, StringComparer.Ordinal)];

    public IReadOnlyList<HookStat> HookStats() =>
        [.. hooks.Select(t => new HookStat(t.Key, t.Value.Count, t.Value.Failed, t.Value.Duration)).OrderByDescending(t => t.Count).ThenBy(t => t.Type, StringComparer.Ordinal)];

    public IReadOnlyList<GuardItem> GuardItems() =>
        [.. guards.Select(g => new GuardItem(g.Key.Item1, g.Key.Item2, g.Value)).OrderByDescending(g => g.Count).ThenBy(g => g.Name, StringComparer.Ordinal)];

    public IReadOnlyList<NamedCount> SkillCounts() =>
        [.. skills.Select(s => new NamedCount(s.Key, s.Value)).OrderByDescending(s => s.Count).ThenBy(s => s.Name, StringComparer.Ordinal)];

    public IReadOnlyList<NamedCount> SubagentCounts() =>
        [.. subagents.Select(s => new NamedCount(s.Key, s.Value)).OrderByDescending(s => s.Count).ThenBy(s => s.Name, StringComparer.Ordinal)];

    public IReadOnlyList<TimelineBucket> BuildTimeline(DateTimeOffset start, DateTimeOffset end)
    {
        if (timeline.Count == 0 || end < start)
        {
            return [];
        }

        var span = (end - start).Ticks;
        var count = span <= 0 ? 1 : MaxBuckets;
        var width = span <= 0 ? 1 : Math.Max(1, (long)Math.Ceiling(span / (double)count));
        var tools = new int[count];
        var hooks = new int[count];
        var guards = new int[count];
        var tokens = new long[count];

        foreach (var e in timeline)
        {
            var index = (int)Math.Clamp((e.Ticks - start.UtcTicks) / width, 0, count - 1);
            switch (e.Kind)
            {
                case EventKind.Tool: tools[index] += (int)e.Amount; break;
                case EventKind.Hook: hooks[index] += (int)e.Amount; break;
                case EventKind.Guard: guards[index] += (int)e.Amount; break;
                default: tokens[index] += e.Amount; break;
            }
        }

        return [.. Enumerable.Range(0, count).Select(i => new TimelineBucket(start.AddTicks(i * width), tools[i], hooks[i], guards[i], tokens[i]))];
    }
}
