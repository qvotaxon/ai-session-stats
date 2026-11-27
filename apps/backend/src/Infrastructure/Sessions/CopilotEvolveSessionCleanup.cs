using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SessionInsights.Application.Common;
using SessionInsights.Domain.Sessions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace SessionInsights.Infrastructure.Sessions;

/// <summary>
/// Deletes the one Copilot session folder an Evolve analysis created, found by the id it pre-assigned with
/// --session-id. Every rule fails closed: when any of them cannot be established, nothing is deleted.
/// The folder must be a GUID-named direct child of the child CLI's own session-state folder, must be no link,
/// and its workspace.yaml must name Evolve's working folder as its cwd. No enumeration, no wildcard.
/// </summary>
internal sealed class CopilotEvolveSessionCleanup(
    IFileSystem fs,
    IOptions<SessionInsightsOptions> options,
    ILogger<CopilotEvolveSessionCleanup> logger) : IEvolveSessionCleanup
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public bool Exists(string sessionId)
    {
        if (!SessionIdFormat.IsValid(sessionId))
        {
            return false;
        }

        try
        {
            var folder = ResolveSessionFolder(sessionId);
            return folder is not null && (fs.Directory.Exists(folder) || fs.File.Exists(folder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    public async Task<EvolveSessionCleanupOutcome> RemoveAsync(string sessionId, bool existedBeforeRun, CancellationToken cancellationToken)
    {
        string? failure = null;
        EvolveSessionCleanupOutcome outcome;
        try
        {
            outcome = await TryRemoveAsync(sessionId, existedBeforeRun, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            outcome = EvolveSessionCleanupOutcome.Failed;
            failure = ex.GetType().Name;
        }

        Log(sessionId, outcome, failure);
        return outcome;
    }

    private async Task<EvolveSessionCleanupOutcome> TryRemoveAsync(string sessionId, bool existedBeforeRun, CancellationToken cancellationToken)
    {
        if (!SessionIdFormat.IsValid(sessionId))
        {
            return EvolveSessionCleanupOutcome.InvalidId;
        }

        if (existedBeforeRun)
        {
            return EvolveSessionCleanupOutcome.ExistedBeforeRun;
        }

        var folder = ResolveSessionFolder(sessionId);
        if (folder is null)
        {
            return EvolveSessionCleanupOutcome.OutsideRoot;
        }

        if (!fs.Directory.Exists(folder))
        {
            return EvolveSessionCleanupOutcome.NotFound;
        }

        if (fs.File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint))
        {
            return EvolveSessionCleanupOutcome.ReparsePoint;
        }

        var workspace = fs.Path.Combine(folder, "workspace.yaml");
        if (!fs.File.Exists(workspace))
        {
            return EvolveSessionCleanupOutcome.NoWorkspace;
        }

        Dictionary<string, object?>? values;
        try
        {
            var text = await fs.File.ReadAllTextAsync(workspace, cancellationToken);
            values = new DeserializerBuilder().Build().Deserialize<Dictionary<string, object?>>(text);
        }
        catch (YamlException)
        {
            return EvolveSessionCleanupOutcome.Unreadable;
        }

        if (values is null)
        {
            return EvolveSessionCleanupOutcome.Unreadable;
        }

        if (values.TryGetValue("id", out var id) && !string.Equals(id?.ToString(), sessionId, StringComparison.OrdinalIgnoreCase))
        {
            return EvolveSessionCleanupOutcome.IdMismatch;
        }

        if (!values.TryGetValue("cwd", out var cwd) || !IsEvolveWorkingDirectory(cwd?.ToString()))
        {
            return EvolveSessionCleanupOutcome.CwdMismatch;
        }

        fs.Directory.Delete(folder, recursive: true);
        return EvolveSessionCleanupOutcome.Removed;
    }

    /// <summary>The session folder directly under the child's session-state folder, or null when the root is not rooted or the id escapes it.</summary>
    private string? ResolveSessionFolder(string sessionId)
    {
        var root = fs.Path.Combine(options.Value.ResolveCopilotHome(), "session-state");
        if (!fs.Path.IsPathFullyQualified(root))
        {
            return null;
        }

        var folder = fs.Path.GetFullPath(fs.Path.Combine(root, sessionId));
        var parent = fs.Path.GetDirectoryName(folder);
        return parent is not null && string.Equals(TrimSeparators(parent), TrimSeparators(fs.Path.GetFullPath(root)), PathComparison)
            ? folder
            : null;
    }

    private static bool IsEvolveWorkingDirectory(string? cwd) =>
        !string.IsNullOrWhiteSpace(cwd)
        && string.Equals(TrimSeparators(cwd), TrimSeparators(SkillAnalysisActivity.WorkingDirectory), PathComparison);

    private static string TrimSeparators(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    // Only the app's own session id, the outcome and an exception type name: never a path, a workspace value or a message.
    private void Log(string sessionId, EvolveSessionCleanupOutcome outcome, string? exceptionType)
    {
        var id = SessionIdFormat.IsValid(sessionId) ? sessionId : "(invalid)";
        if (outcome == EvolveSessionCleanupOutcome.Removed)
        {
            logger.LogInformation("Removed Evolve's Copilot session {SessionId}", id);
        }
        else if (exceptionType is null)
        {
            logger.LogWarning("Kept Evolve's Copilot session {SessionId}: {Outcome}", id, outcome);
        }
        else
        {
            logger.LogWarning("Kept Evolve's Copilot session {SessionId}: {Outcome} ({ExceptionType})", id, outcome, exceptionType);
        }
    }
}
