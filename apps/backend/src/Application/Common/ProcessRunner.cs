using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SessionInsights.Application.Common;

/// <param name="FileName">The executable to start.</param>
/// <param name="Arguments">The argument list, passed as separate elements.</param>
/// <param name="WorkingDirectory">The child's working directory.</param>
/// <param name="Environment">Variables to set for the child.</param>
/// <param name="StandardInput">Text written to the child's standard input, or null for none.</param>
/// <param name="RemoveEnvironment">Names of inherited environment variables to remove from the child. Applied after <paramref name="Environment"/>, so a name in both ends up absent.</param>
public sealed record ProcessSpec(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string>? Environment = null,
    string? StandardInput = null,
    IReadOnlyCollection<string>? RemoveEnvironment = null);

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

/// <summary>Runs an external process. Cancellation kills the whole process tree and throws OperationCanceledException.</summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken);
}

/// <summary>Resolves a CLI name (or configured path) to an executable path, or null when it cannot be found.</summary>
public interface IExecutableLocator
{
    string? Locate(string nameOrPath);
}
