using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        return [.. snapshot!.Where(s => !excluded.Contains(s.Id) && !s.LooksLikeReportFork)];
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
                else if (runActive)
                {
                    // Race safety: a session file that appears while a report run is active may be that run's fork.
                    // It stays invisible until the run ends and its origin is resolved (exclusion set or heuristic).
                    logger.LogDebug("Holding back new {Source} session {SessionId} while a report run is active", reader.Source, group.SessionId);
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
            cache[key] = (signature, session);
        }

        foreach (var stale in cache.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            cache.Remove(stale);
        }

        snapshot = [.. cache.Values.Select(v => v.Session).OfType<Session>().DistinctBy(s => s.Id, StringComparer.OrdinalIgnoreCase)];
        refreshedAt = now;
        logger.LogInformation("Session index refreshed: {SessionCount} sessions, {ReparsedCount} parsed", snapshot.Count, work.Count);
    }
}

/// <summary>Keeps the index warm so the first request does not pay for the initial parse.</summary>
internal sealed class SessionIndexWarmer(ISessionIndex index, IOptions<SessionInsightsOptions> options, ILogger<SessionIndexWarmer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.IndexRefreshInterval);
        do
        {
            try
            {
                await index.GetAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
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
