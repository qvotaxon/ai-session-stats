using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using SessionInsights.Application.Common;
using SessionInsights.Infrastructure.Sessions;
using Xunit;
using static SessionInsights.Infrastructure.Tests.TestSupport;

namespace SessionInsights.Infrastructure.Tests;

public sealed class HarnessInventoryTests
{
    [Fact]
    public async Task Inventory_only_lists_plugins_and_caches_allowlisted_metadata()
    {
        var locator = Substitute.For<IExecutableLocator>();
        locator.Locate(Arg.Any<string>()).Returns(c => $"/bin/{c.Arg<string>()}");
        var processes = Substitute.For<IProcessRunner>();
        processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>()).Returns(c =>
        {
            var source = c.Arg<ProcessSpec>().FileName;
            return Task.FromResult(new ProcessResult(0,
                source.EndsWith("claude", StringComparison.Ordinal)
                    ? """[{"id":"msdc-harness@msdc-plugins","version":"0.26.1","scope":"user","enabled":true,"installPath":"private"}]"""
                    : """{"plugins":[{"name":"msdc-harness","marketplace":"msdc-plugins","version":"0.24.0","enabled":false},{"name":"msdc-harness","marketplace":"other","version":"9.9.9"}]}""", ""));
        });
        var inventory = new HarnessInventory(locator, processes, Options(), TimeProvider.System, NullLogger<HarnessInventory>.Instance);

        var result = await inventory.GetAsync(TestContext.Current.CancellationToken);
        await inventory.GetAsync(TestContext.Current.CancellationToken);

        result.Select(r => r.Status).ShouldBe(["installed", "installed"]);
        result[0].Installations.Single().Version.ShouldBe("0.26.1");
        result[1].Installations.Single().Enabled.ShouldBe(false);
        await processes.Received(2).RunAsync(Arg.Is<ProcessSpec>(p =>
            p.Arguments.SequenceEqual(new[] { "plugin", "list", "--json" })), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, """{"unexpected":"data"}""")]
    [InlineData(0, "not json")]
    [InlineData(1, "")]
    [InlineData(0, """[{"id":"msdc-harness@msdc-plugins","version":null}]""")]
    public async Task Inventory_errors_are_not_reported_as_missing_plugins(int exitCode, string stdout)
    {
        var locator = Substitute.For<IExecutableLocator>();
        locator.Locate(Arg.Any<string>()).Returns("/bin/cli");
        var processes = Substitute.For<IProcessRunner>();
        processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(exitCode, stdout, "sensitive stderr"));
        var inventory = new HarnessInventory(locator, processes, Options(), TimeProvider.System, NullLogger<HarnessInventory>.Instance);

        var result = await inventory.GetAsync(TestContext.Current.CancellationToken);

        result.ShouldAllBe(r => r.Status == "error" && r.Error != null);
        result.ShouldAllBe(r => !r.Error!.Contains("sensitive", StringComparison.Ordinal));
    }
}
