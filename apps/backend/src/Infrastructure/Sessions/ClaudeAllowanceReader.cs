using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SessionInsights.Application.Common;
using System.IO.Abstractions;

namespace SessionInsights.Infrastructure.Sessions;

public sealed class ClaudeAllowanceReader(
    IFileSystem fs,
    IOptions<SessionInsightsOptions> options,
    TimeProvider timeProvider,
    ILogger<ClaudeAllowanceReader> logger) : IClaudeAllowanceReader
{
    private static readonly TimeSpan Freshness = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly string path = fs.Path.Combine(options.Value.ResolveDataRoot(), "claude-allowance.json");

    public ClaudeAllowanceDto Get()
    {
        if (!fs.File.Exists(path))
        {
            return Unavailable("Connect Claude Code's status line to provide subscription usage limits.");
        }

        ClaudeAllowanceSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<ClaudeAllowanceSnapshot>(fs.File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read Claude allowance statusline data");
            return new ClaudeAllowanceDto("error", null, null, null, "Saved statusline data could not be read.");
        }

        if (snapshot is null || !IsValid(snapshot))
        {
            logger.LogWarning("Claude allowance statusline data is invalid");
            return new ClaudeAllowanceDto("error", null, null, null, "Saved statusline data is invalid.");
        }

        if (snapshot.FiveHour is null && snapshot.SevenDay is null)
        {
            return Unavailable("Claude Code has not reported subscription usage limits.");
        }

        var status = timeProvider.GetUtcNow() - snapshot.UpdatedAt > Freshness ? "stale" : "available";
        var message = status == "stale" ? "No recent Claude Code statusline update; shown values may be out of date." : null;
        return new ClaudeAllowanceDto(status, snapshot.UpdatedAt, snapshot.FiveHour, snapshot.SevenDay, message);
    }

    private static ClaudeAllowanceDto Unavailable(string message) =>
        new("unavailable", null, null, null, message);

    private static bool IsValid(ClaudeAllowanceSnapshot snapshot) =>
        IsValid(snapshot.FiveHour) && IsValid(snapshot.SevenDay);

    private static bool IsValid(ClaudeAllowanceWindow? window) =>
        window is null || (double.IsFinite(window.UsedPercentage) && window.UsedPercentage is >= 0 and <= 100);
}
