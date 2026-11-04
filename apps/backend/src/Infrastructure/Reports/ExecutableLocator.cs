using System;
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

        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(fs.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var directory in directories)
        {
            foreach (var extension in Extensions.Prepend(string.Empty))
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
}
