using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace SessionInsights.Infrastructure.Tests;

internal sealed record LogEntry(LogLevel Level, string Template, IReadOnlyDictionary<string, object?> Properties, string Message);

/// <summary>Records every log call with its level, message template, named properties and formatted message.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly object gate = new();
    private readonly List<LogEntry> entries = [];

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (gate)
            {
                return [.. entries];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var properties = new Dictionary<string, object?>();
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (var pair in pairs)
            {
                properties[pair.Key] = pair.Value;
            }
        }

        var template = properties.TryGetValue("{OriginalFormat}", out var format) ? format?.ToString() ?? string.Empty : string.Empty;
        properties.Remove("{OriginalFormat}");
        lock (gate)
        {
            entries.Add(new LogEntry(logLevel, template, properties, formatter(state, exception)));
        }
    }

    public IEnumerable<string> AllText() =>
        Entries.SelectMany(entry => new[] { entry.Template, entry.Message }.Concat(entry.Properties.Values.Select(value => value?.ToString() ?? string.Empty)));
}
