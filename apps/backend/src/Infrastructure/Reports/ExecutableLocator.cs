using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using SessionInsights.Application.Common;

namespace SessionInsights.Infrastructure.Reports;

internal sealed class ExecutableLocator(IFileSystem fs) : IExecutableLocator
{
    private static readonly string[] Extensions = [".exe", ".com", ".cmd", ".bat"];

    public string? Locate(string nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath))
        {
            return null;
        }

        if (nameOrPath.IndexOfAny(['\\', '/']) >= 0)
        {
            return fs.File.Exists(nameOrPath) ? nameOrPath : null;
        }

        var directories = SearchDirectories().ToList();

        foreach (var directory in directories)
        {
            // npm drops an extensionless sh shim next to the .cmd one. Windows cannot start it (error 193), so it is tried last.
            foreach (var extension in Extensions.Append(string.Empty))
            {
                var candidate = fs.Path.Combine(directory, nameOrPath + extension);
                if (fs.File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    // The backend can be started from a process with a stale or reduced PATH (service, launcher, IDE), so the
    // persisted user/machine PATH and the well-known CLI install folders are searched as well.
    private IEnumerable<string> SearchDirectories()
    {
        var sources = new List<string?> { Environment.GetEnvironmentVariable("PATH") };
        if (OperatingSystem.IsWindows())
        {
            sources.Add(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User));
            sources.Add(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine));
        }

        var fromPath = sources
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .SelectMany(source => source!.Split(fs.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(entry => Environment.ExpandEnvironmentVariables(entry.Trim('"')));

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        List<string> wellKnown = [];
        if (!string.IsNullOrEmpty(home))
        {
            wellKnown.Add(fs.Path.Combine(home, ".local", "bin"));
            wellKnown.Add(fs.Path.Combine(home, ".claude", "local"));
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
        {
            wellKnown.Add(fs.Path.Combine(appData, "npm"));
        }

        return fromPath.Concat(wellKnown).Where(directory => !string.IsNullOrWhiteSpace(directory)).Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
