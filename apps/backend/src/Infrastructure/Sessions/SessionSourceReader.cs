using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using SessionInsights.Domain.Sessions;

namespace SessionInsights.Infrastructure.Sessions;

/// <summary>The files that make up one session and a signature (path+mtime+size) used as the parse-cache key.</summary>
public sealed record SessionFileGroup(string SessionId, string Signature, IReadOnlyList<string> Files)
{
    public static string SignatureOf(IFileSystem fs, IEnumerable<string> files) =>
        string.Join('|', files.Select(f =>
        {
            var info = fs.FileInfo.New(f);
            return $"{f};{info.LastWriteTimeUtc.Ticks};{info.Length}";
        }));
}

public interface ISessionSourceReader
{
    SessionSource Source { get; }

    IReadOnlyList<SessionFileGroup> Discover();

    Session? Parse(SessionFileGroup group, DateTimeOffset now);
}

internal sealed class ModelAggregate
{
    private readonly Dictionary<string, (long Requests, TokenUsage Tokens)> values = new(StringComparer.Ordinal);

    public IEnumerable<(string Model, long Requests, TokenUsage Tokens)> Entries => values.Select(v => (v.Key, v.Value.Requests, v.Value.Tokens));

    public void Add(string model, long requests, TokenUsage tokens)
    {
        values.TryGetValue(model, out var cur);
        values[model] = (cur.Requests + requests, (cur.Tokens ?? TokenUsage.Empty).Plus(tokens));
    }

    /// <summary>Keeps the per-field maximum: for cumulative totals that are reported again on every shutdown.</summary>
    public void Max(string model, long requests, TokenUsage tokens)
    {
        values.TryGetValue(model, out var cur);
        values[model] = (Math.Max(cur.Requests, requests), (cur.Tokens ?? TokenUsage.Empty).MaxWith(tokens));
    }

    public void AddAll(ModelAggregate other)
    {
        foreach (var (model, v) in other.values)
        {
            Add(model, v.Requests, v.Tokens);
        }
    }

    public ModelAggregate Minus(ModelAggregate other)
    {
        var result = new ModelAggregate();
        foreach (var (model, v) in values)
        {
            other.values.TryGetValue(model, out var o);
            result.values[model] = (Math.Max(0, v.Requests - o.Requests), v.Tokens.MinusClamped(o.Tokens ?? TokenUsage.Empty));
        }

        return result;
    }

    public ModelAggregate Merge(ModelAggregate other)
    {
        var result = new ModelAggregate();
        result.AddAll(this);
        result.AddAll(other);
        return result;
    }
}

