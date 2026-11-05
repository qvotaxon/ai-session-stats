using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SessionInsights.Application.Common;
using SessionInsights.Domain.Reports;
using SessionInsights.Domain.Sessions;
using SessionInsights.Infrastructure.Sessions;

namespace SessionInsights.Infrastructure.Reports;

internal sealed class ReportActivityTracker : IReportActivity
{
    private int active;

    public bool HasActiveRun => Volatile.Read(ref active) > 0;

    public void Begin() => Interlocked.Increment(ref active);

    public void End() => Interlocked.Decrement(ref active);
}

/// <summary>
/// Owns report runs: one active run per session (409 otherwise), a single worker executes them in order through
/// <see cref="IProcessRunner"/>. Runs and markdown are persisted under DataRoot\reports\&lt;sessionId&gt;\v&lt;n&gt;.*.
/// </summary>
internal sealed class ReportCoordinator : IReportCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private sealed class RunState(ReportRun run)
    {
        public ReportRun Run { get; } = run;
        public CancellationTokenSource Cancel { get; } = new();
        public bool UserCancelled { get; set; }
        public bool Ended { get; set; }
    }

    private readonly IFileSystem fs;
    private readonly ISessionIndex index;
    private readonly IExcludedSessions excluded;
    private readonly IProcessRunner processes;
    private readonly IExecutableLocator locator;
    private readonly ReportActivityTracker tracker;
    private readonly IOptions<SessionInsightsOptions> options;
    private readonly TimeProvider clock;
    private readonly ILogger<ReportCoordinator> logger;
    private readonly string root;
    private readonly Lock gate = new();
    private readonly Dictionary<string, RunState> runs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Channel<string> queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

    public ReportCoordinator(
        IFileSystem fs,
        ISessionIndex index,
        IExcludedSessions excluded,
        IProcessRunner processes,
        IExecutableLocator locator,
        ReportActivityTracker tracker,
        IOptions<SessionInsightsOptions> options,
        TimeProvider clock,
        ILogger<ReportCoordinator> logger)
    {
        this.fs = fs;
        this.index = index;
        this.excluded = excluded;
        this.processes = processes;
        this.locator = locator;
        this.tracker = tracker;
        this.options = options;
        this.clock = clock;
        this.logger = logger;
        root = fs.Path.Combine(options.Value.ResolveDataRoot(), "reports");
        LoadPersistedRuns();
    }

    public ChannelReader<string> Queue => queue.Reader;

    public async Task<ReportRun> StartAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (!SessionIdFormat.IsValid(sessionId))
        {
            throw new RequestValidationException("id must be a GUID.");
        }

        // The index never returns forks or excluded sessions, so those can never be reported.
        var session = await index.FindAsync(sessionId, cancellationToken)
            ?? throw new NotFoundException($"Session {sessionId} was not found.");

        RunState state;
        lock (gate)
        {
            if (runs.Values.Any(r => r.Run.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase) && r.Run.IsActive))
            {
                throw new ConflictException($"A report run for session {sessionId} is already queued or running.");
            }

            var version = runs.Values.Where(r => r.Run.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase)).Select(r => r.Run.Version).DefaultIfEmpty(0).Max() + 1;
            state = new RunState(new ReportRun
            {
                Id = Guid.NewGuid().ToString("D"),
                SessionId = session.Id,
                Version = version,
                Status = ReportStatus.Queued,
                Cli = session.Source,
                Forked = session.Source == SessionSource.Claude,
                StartedAt = clock.GetUtcNow(),
            });
            runs[state.Run.Id] = state;
            tracker.Begin();
            Persist(state.Run);
        }

        logger.LogInformation("Report run {RunId} (v{Version}) queued for session {SessionId}", state.Run.Id, state.Run.Version, session.Id);
        await queue.Writer.WriteAsync(state.Run.Id, cancellationToken);
        return state.Run;
    }

    public IReadOnlyList<ReportRun> List(string sessionId)
    {
        lock (gate)
        {
            return [.. runs.Values.Select(s => s.Run).Where(r => r.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase)).OrderByDescending(r => r.Version)];
        }
    }

    public ReportRun? Find(string sessionId, string runId)
    {
        lock (gate)
        {
            return runs.TryGetValue(runId, out var s) && s.Run.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase) ? s.Run : null;
        }
    }

    public ReportStatus? LatestStatus(string sessionId) => List(sessionId).FirstOrDefault()?.Status;

    public async Task<string?> ReadContentAsync(ReportRun run, CancellationToken cancellationToken)
    {
        var path = MarkdownPath(run);
        return fs.File.Exists(path) ? await fs.File.ReadAllTextAsync(path, cancellationToken) : null;
    }

    public ReportRun Cancel(string sessionId, string runId)
    {
        RunState state;
        lock (gate)
        {
            state = runs.TryGetValue(runId, out var s) && s.Run.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase)
                ? s
                : throw new NotFoundException($"Report run {runId} was not found.");
            if (!state.Run.IsActive)
            {
                throw new ConflictException("The report run is not running.");
            }

            state.UserCancelled = true;
        }

        state.Cancel.Cancel();
        logger.LogInformation("Cancellation requested for report run {RunId}", runId);
        return state.Run;
    }

    /// <summary>Executes one queued run. Called by <see cref="ReportWorker"/>; public to the assembly so tests can drive it.</summary>
    public async Task ExecuteAsync(string runId, CancellationToken hostToken)
    {
        RunState state;
        lock (gate)
        {
            if (!runs.TryGetValue(runId, out state!) || state.Ended)
            {
                return;
            }
        }

        var run = state.Run;
        if (state.UserCancelled)
        {
            Finish(state, ReportStatus.Cancelled, null, null, "Cancelled before it started.");
            return;
        }

        lock (gate)
        {
            run.Status = ReportStatus.Running;
            Persist(run);
        }

        using var timeout = new CancellationTokenSource(options.Value.ReportTimeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(hostToken, state.Cancel.Token, timeout.Token);
        var started = clock.GetTimestamp();

        try
        {
            var session = await index.FindAsync(run.SessionId, linked.Token)
                ?? throw new ReportFailure("The session is no longer available.");
            if (string.IsNullOrWhiteSpace(session.Cwd) || !fs.Directory.Exists(session.Cwd))
            {
                throw new ReportFailure("The session's working directory no longer exists.");
            }

            var cliName = run.Cli == SessionSource.Claude ? options.Value.ClaudeCliPath : options.Value.CopilotCliPath;
            var exe = locator.Locate(cliName) ?? throw new ReportFailure($"CLI '{cliName}' was not found.");
            var result = await processes.RunAsync(new ProcessSpec(exe, BuildArguments(run), session.Cwd, BuildEnvironment(run)), linked.Token);

            lock (gate)
            {
                run.ExitCode = result.ExitCode;
            }

            string markdown;
            if (run.Cli == SessionSource.Claude)
            {
                var (text, forkId, isError) = ParseClaudeOutput(result.StdOut);
                if (forkId is not null)
                {
                    lock (gate)
                    {
                        run.ForkedSessionId = forkId;
                    }

                    excluded.Add(forkId);
                }

                if (result.ExitCode != 0 || isError || string.IsNullOrWhiteSpace(text))
                {
                    throw new ReportFailure(result.ExitCode != 0 ? $"The CLI exited with code {result.ExitCode}." : "The CLI returned no report.");
                }

                markdown = text;
            }
            else
            {
                if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StdOut))
                {
                    throw new ReportFailure(result.ExitCode != 0 ? $"The CLI exited with code {result.ExitCode}." : "The CLI returned no report.");
                }

                markdown = result.StdOut.Trim();
            }

            fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(MarkdownPath(run))!);
            await fs.File.WriteAllTextAsync(MarkdownPath(run), markdown, CancellationToken.None);
            Finish(state, ReportStatus.Succeeded, clock.GetElapsedTime(started), result.ExitCode, null);
        }
        catch (OperationCanceledException)
        {
            var elapsed = clock.GetElapsedTime(started);
            if (state.UserCancelled)
            {
                Finish(state, ReportStatus.Cancelled, elapsed, null, "Cancelled by the user.");
            }
            else if (timeout.IsCancellationRequested)
            {
                Finish(state, ReportStatus.Failed, elapsed, null, "The report run timed out.");
            }
            else
            {
                Finish(state, ReportStatus.Failed, elapsed, null, "interrupted");
            }
        }
        catch (ReportFailure ex)
        {
            Finish(state, ReportStatus.Failed, clock.GetElapsedTime(started), null, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogError(ex, "Report run {RunId} failed unexpectedly", runId);
            Finish(state, ReportStatus.Failed, clock.GetElapsedTime(started), null, "The report run failed unexpectedly.");
        }
    }

    internal static IReadOnlyList<string> BuildArguments(ReportRun run) => run.Cli == SessionSource.Claude
        ? ["-p", ReportConstants.Prompt, "--resume", run.SessionId, "--fork-session", "--output-format", "json"]
        : ["-p", ReportConstants.Prompt, $"--resume={run.SessionId}", "-s"];

    private Dictionary<string, string>? BuildEnvironment(ReportRun run)
    {
        var (name, dir) = run.Cli == SessionSource.Claude
            ? ("CLAUDE_CONFIG_DIR", options.Value.ClaudeConfigDir)
            : ("COPILOT_HOME", options.Value.CopilotHome);
        return string.IsNullOrWhiteSpace(dir) ? null : new Dictionary<string, string> { [name] = dir };
    }

    private static (string? Text, string? ForkId, bool IsError) ParseClaudeOutput(string stdout)
    {
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            JsonElement? root = doc.RootElement;
            var forkId = root.Str("session_id");
            return (root.Str("result"), SessionIdFormat.IsValid(forkId) ? forkId : null, root.Bool("is_error") == true);
        }
        catch (JsonException)
        {
            return (null, null, true);
        }
    }

    private void Finish(RunState state, ReportStatus status, TimeSpan? elapsed, int? exitCode, string? error)
    {
        lock (gate)
        {
            if (state.Ended)
            {
                return;
            }

            state.Ended = true;
            var run = state.Run;
            run.Status = status;
            run.FinishedAt = clock.GetUtcNow();
            run.DurationMs = elapsed is { } e ? (long)e.TotalMilliseconds : (long)(run.FinishedAt.Value - run.StartedAt).TotalMilliseconds;
            run.ExitCode = exitCode ?? run.ExitCode;
            run.Error = error;
            Persist(run);
        }

        // Exclusion (above) is recorded before the activity flag drops, so the index never lists the fork in between.
        tracker.End();
        logger.LogInformation("Report run {RunId} finished as {Status}", state.Run.Id, status);
    }

    private string MarkdownPath(ReportRun run) => fs.Path.Combine(root, run.SessionId, $"v{run.Version}.md");

    private void Persist(ReportRun run)
    {
        var path = fs.Path.Combine(root, run.SessionId, $"v{run.Version}.json");
        fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(path)!);
        fs.File.WriteAllText(path, JsonSerializer.Serialize(run, JsonOptions));
    }

    private void LoadPersistedRuns()
    {
        if (!fs.Directory.Exists(root))
        {
            return;
        }

        foreach (var file in fs.Directory.EnumerateFiles(root, "v*.json", SearchOption.AllDirectories))
        {
            try
            {
                var run = JsonSerializer.Deserialize<ReportRun>(fs.File.ReadAllText(file), JsonOptions);
                if (run is null || !SessionIdFormat.IsValid(run.SessionId) || !Guid.TryParse(run.Id, out _))
                {
                    continue;
                }

                if (run.IsActive)
                {
                    run.Status = ReportStatus.Failed;
                    run.Error = "interrupted";
                    run.FinishedAt = clock.GetUtcNow();
                    Persist(run);
                }

                if (run.ForkedSessionId is not null)
                {
                    excluded.Add(run.ForkedSessionId);
                }

                runs[run.Id] = new RunState(run) { Ended = true };
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                logger.LogWarning(ex, "Skipping unreadable report run file {FileName}", fs.Path.GetFileName(file));
            }
        }
    }

    private sealed class ReportFailure(string message) : Exception(message);
}

internal sealed class ReportWorker(ReportCoordinator coordinator, ILogger<ReportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var runId in coordinator.Queue.ReadAllAsync(stoppingToken))
            {
                await coordinator.ExecuteAsync(runId, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Report worker stopping");
        }
    }
}

