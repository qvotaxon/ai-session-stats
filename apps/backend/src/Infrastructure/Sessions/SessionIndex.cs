using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SessionInsights.Application.Common;
using SessionInsights.Domain.Sessions;

namespace SessionInsights.Infrastructure.Sessions;

/// <summary>
/// In-memory index of all sessions, rebuilt incrementally: a session is only re-parsed when the signature
/// (path + mtime + size of its files) changed. Report forks are never part of the result.
/// </summary>
internal sealed class SessionIndex(
    IEnumerable<ISessionSourceReader> readers,
    IExcludedSessions excluded,
    IReportActivity activity,
    ISkillAnalysisActivity skillAnalysis,
    IOptions<SessionInsightsOptions> options,
    TimeProvider clock,
    ILogger<SessionIndex> logger) : ISessionIndex
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, (string Signature, Session? Session)> cache = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<Session>? snapshot;
    private DateTimeOffset refreshedAt;

    public async ValueTask<IReadOnlyList<Session>> GetAllAsync(CancellationToken cancellationToken)
    {
        await EnsureFreshAsync(cancellationToken);
        return [.. snapshot!.Where(s => !excluded.Contains(s.Id) && !s.LooksLikeReportFork && !IsSkillAnalysisSession(s))];
    }

    public async ValueTask<Session?> FindAsync(string id, CancellationToken cancellationToken) =>
        (await GetAllAsync(cancellationToken)).FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private async Task EnsureFreshAsync(CancellationToken cancellationToken)
    {
        if (snapshot is not null && clock.GetUtcNow() - refreshedAt < options.Value.IndexRefreshInterval)
        {
            return;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (snapshot is not null && clock.GetUtcNow() - refreshedAt < options.Value.IndexRefreshInterval)
            {
                return;
            }

            await RefreshAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var runActive = activity.HasActiveRun;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var work = new List<(ISessionSourceReader Reader, SessionFileGroup Group)>();

        foreach (var reader in readers)
        {
            foreach (var group in reader.Discover())
            {
                var key = $"{reader.Source}:{group.SessionId}";
                if (cache.TryGetValue(key, out var entry))
                {
                    seen.Add(key);
                    if (entry.Signature != group.Signature)
                    {
                        work.Add((reader, group));
                    }
                }
                else if (runActive || skillAnalysis.HasActiveAnalysis)
                {
                    // A session file that appears during report or skill analysis is held back until its origin is known.
                    logger.LogDebug("Holding back new {Source} session {SessionId} while a report run or skill analysis is active", reader.Source, group.SessionId);
                }
                else
                {
                    seen.Add(key);
                    work.Add((reader, group));
                }
            }
        }

        var parsed = new (string Key, string Signature, Session? Session)[work.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, work.Count),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(4, Environment.ProcessorCount)), CancellationToken = cancellationToken },
            (i, ct) =>
            {
                var (reader, group) = work[i];
                parsed[i] = ($"{reader.Source}:{group.SessionId}", group.Signature, reader.Parse(group, now));
                return ValueTask.CompletedTask;
            });

        foreach (var (key, signature, session) in parsed)
        {
            cache[key] = (signature, IsSkillAnalysisSession(session) ? null : session);
        }

        foreach (var stale in cache.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            cache.Remove(stale);
        }

        snapshot = [.. cache.Values.Select(v => v.Session).OfType<Session>().DistinctBy(s => s.Id, StringComparer.OrdinalIgnoreCase)];
        refreshedAt = now;
        logger.LogInformation("Session index refreshed: {SessionCount} sessions, {ReparsedCount} parsed", snapshot.Count, work.Count);
    }

    private static bool IsSkillAnalysisSession(Session? session)
    {
        if (session?.Cwd is not { } cwd)
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(
            cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            SkillAnalysisActivity.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            comparison);
    }
}

/// <summary>Initializes application data after the listener starts, then keeps the index warm.</summary>
internal sealed class SessionIndexWarmer(
    IServiceProvider services,
    IHostApplicationLifetime lifetime,
    ApplicationReadiness readiness,
    IOptions<SessionInsightsOptions> options,
    ILogger<SessionIndexWarmer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        try
        {
            await started.Task.WaitAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.IndexRefreshInterval);
        do
        {
            try
            {
                // Restore report exclusions before publishing the initial session snapshot.
                _ = services.GetRequiredService<IReportCoordinator>();
                var index = services.GetRequiredService<ISessionIndex>();
                await index.GetAllAsync(stoppingToken);
                readiness.MarkReady();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (readiness.Status != "ready")
                {
                    readiness.MarkFailed();
                }

                logger.LogError(ex, "Index refresh failed");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
