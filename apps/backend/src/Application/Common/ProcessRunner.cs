using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SessionInsights.Application.Common;

public sealed record ProcessSpec(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string>? Environment = null);

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
