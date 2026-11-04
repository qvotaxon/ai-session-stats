using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace SessionInsights.Infrastructure.Sessions;

internal static class JsonExtensions
{
    public static JsonElement? Get(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v : null;

    public static JsonElement? Get(this JsonElement? e, string name) => e is { } v ? v.Get(name) : null;

    public static string? Str(this JsonElement e, string name) => ((JsonElement?)e).Str(name);

    public static bool? Bool(this JsonElement e, string name) => ((JsonElement?)e).Bool(name);

    public static string? Str(this JsonElement? e, string name) =>
        e.Get(name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    public static long Num(this JsonElement? e, string name) =>
        e.Get(name) is { } v && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : e.Get(name) is { ValueKind: JsonValueKind.Number } d ? (long)d.GetDouble() : 0;

    public static double Dbl(this JsonElement? e, string name) =>
        e.Get(name) is { ValueKind: JsonValueKind.Number } v ? v.GetDouble() : 0;

    public static bool? Bool(this JsonElement? e, string name) => e.Get(name) is { } v && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    public static DateTimeOffset? Time(this JsonElement? e, string name) =>
        e.Str(name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;
}

internal static class JsonLines
{
    /// <summary>Streams a JSONL file line by line. Malformed or truncated lines are skipped. Lines whose "type" is rejected by
    /// <paramref name="wantsLine"/> (inspected on the raw text, before parsing) are never parsed.</summary>
    public static IEnumerable<JsonDocument> Read(Stream stream, Func<string, bool>? wantsLine = null)
    {
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || (wantsLine is not null && !wantsLine(line)))
            {
                continue;
            }

            JsonDocument? doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            yield return doc;
        }
    }
}

