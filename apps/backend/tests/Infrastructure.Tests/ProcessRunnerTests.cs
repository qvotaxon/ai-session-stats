using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SessionInsights.Application.Common;
using SessionInsights.Infrastructure.Reports;
using Shouldly;
using Xunit;

namespace SessionInsights.Infrastructure.Tests;

public sealed class ProcessRunnerTests
{
    private static readonly string FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/usr/bin/env";
    private static readonly string[] Arguments = OperatingSystem.IsWindows() ? ["/d", "/c", "set"] : [];

    [Fact]
    public async Task Removes_the_named_inherited_variable_and_keeps_the_others()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var removed = $"SI_RM_{suffix}";
        var kept = $"SI_KEEP_{suffix}";
        Environment.SetEnvironmentVariable(removed, "x");
        Environment.SetEnvironmentVariable(kept, "y");
        try
        {
            // Windows environment names are case-insensitive, so a lower-case name must still remove it.
            var name = OperatingSystem.IsWindows() ? removed.ToLowerInvariant() : removed;

            var result = await new ProcessRunner().RunAsync(
                new ProcessSpec(FileName, Arguments, Path.GetTempPath(), null, null, [name]),
                TestContext.Current.CancellationToken);

            result.ExitCode.ShouldBe(0);
            result.StdOut.ShouldNotContain(removed, Case.Insensitive);
            result.StdOut.ShouldContain(kept);
        }
        finally
        {
            Environment.SetEnvironmentVariable(removed, null);
            Environment.SetEnvironmentVariable(kept, null);
        }
    }

    [Fact]
    public async Task Removal_wins_over_a_variable_set_for_the_child()
    {
        var name = $"SI_BOTH_{Guid.NewGuid():N}";

        var result = await new ProcessRunner().RunAsync(
            new ProcessSpec(FileName, Arguments, Path.GetTempPath(), new Dictionary<string, string> { [name] = "z" }, null, [name]),
            TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(0);
        result.StdOut.ShouldNotContain(name, Case.Insensitive);
    }

    [Fact]
    public async Task Leaves_the_environment_alone_when_nothing_is_to_be_removed()
    {
        var name = $"SI_KEEP_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(name, "y");
        try
        {
            var result = await new ProcessRunner().RunAsync(
                new ProcessSpec(FileName, Arguments, Path.GetTempPath()),
                TestContext.Current.CancellationToken);

            result.StdOut.ShouldContain(name);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
