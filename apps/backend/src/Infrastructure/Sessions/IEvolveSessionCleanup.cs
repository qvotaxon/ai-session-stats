using System.Threading;
using System.Threading.Tasks;

namespace SessionInsights.Infrastructure.Sessions;

internal enum EvolveSessionCleanupOutcome
{
    Removed,
    NotFound,
    InvalidId,
    OutsideRoot,
    ExistedBeforeRun,
    ReparsePoint,
    NoWorkspace,
    Unreadable,
    CwdMismatch,
    IdMismatch,
    Failed,
}

/// <summary>Removes the Copilot session folder that one Evolve analysis created, and nothing else.</summary>
internal interface IEvolveSessionCleanup
{
    /// <summary>True when something with this id already exists in the child's Copilot session-state folder.</summary>
    bool Exists(string sessionId);

    Task<EvolveSessionCleanupOutcome> RemoveAsync(string sessionId, bool existedBeforeRun, CancellationToken cancellationToken);
}
