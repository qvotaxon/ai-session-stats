using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using SessionInsights.Infrastructure.Reports;
using Shouldly;
using Xunit;

namespace SessionInsights.Infrastructure.Tests;

public sealed class ExecutableLocatorTests
{
    [Fact]
    public void Prefers_the_cmd_shim_over_the_extensionless_npm_shell_script()
    {
        var npm = Path.Combine(Path.GetTempPath(), "npm-bin");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(npm, "copilot")] = new(string.Empty),
            [Path.Combine(npm, "copilot.cmd")] = new(string.Empty),
        });

        var located = WithPath(npm, () => new ExecutableLocator(fs).Locate("copilot"));

        located.ShouldBe(Path.Combine(npm, "copilot.cmd"));
    }

    [Fact]
    public void Falls_back_to_the_extensionless_file_when_nothing_else_matches()
    {
        var bin = Path.Combine(Path.GetTempPath(), "other-bin");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [Path.Combine(bin, "tool")] = new(string.Empty) });

        WithPath(bin, () => new ExecutableLocator(fs).Locate("tool")).ShouldBe(Path.Combine(bin, "tool"));
    }

    private static string? WithPath(string path, Func<string?> action)
    {
        var original = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", path);
        try
        {
            return action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", original);
        }
    }
}
