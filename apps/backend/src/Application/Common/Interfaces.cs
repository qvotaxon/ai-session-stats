using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SessionInsights.Domain.Reports;
using SessionInsights.Domain.Sessions;

namespace SessionInsights.Application.Common;

public interface ISessionIndex
{
    /// <summary>All listable sessions. Report forks are never part of the result.</summary>
    ValueTask<IReadOnlyList<Session>> GetAllAsync(CancellationToken cancellationToken);

    ValueTask<Session?> FindAsync(string id, CancellationToken cancellationToken);
}

public interface IExcludedSessions
{
    bool Contains(string sessionId);

    void Add(string sessionId);
}

/// <summary>Tells the index whether a report run is active, so new session files can be held back until their origin is known.</summary>
public interface IReportActivity
{
    bool HasActiveRun { get; }
}

public interface IReportCoordinator
{
    Task<ReportRun> StartAsync(string sessionId, CancellationToken cancellationToken);

    IReadOnlyList<ReportRun> List(string sessionId);

    ReportRun? Find(string sessionId, string runId);

    ReportStatus? LatestStatus(string sessionId);

    Task<string?> ReadContentAsync(ReportRun run, CancellationToken cancellationToken);

    ReportRun Cancel(string sessionId, string runId);
}

/// <summary>Persisted user preference for which terminal "Continue in CLI" opens. Always a supported terminal id.</summary>
public interface ITerminalSettings
{
    string Terminal { get; }

    void SetTerminal(string id);
}

public interface ITerminalLauncher
{
    /// <summary>Ids of the terminal options that can be launched on this machine.</summary>
    IReadOnlyList<string> Available();

    bool FolderExists(string path);

    /// <summary>Opens a terminal in the working directory running the CLI with the given arguments. Throws <see cref="UnprocessableException"/> when it cannot.</summary>
    void Launch(string terminalId, string workingDirectory, string cli, IReadOnlyList<string> cliArguments);
}

public interface IValidatable
{
    /// <summary>Returns an error message, or null when the request is valid.</summary>
    string? Validate();
}
