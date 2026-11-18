using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SessionInsights.Application.Common;
using SessionInsights.Application.Sessions;
using SessionInsights.Infrastructure.Sessions;
using Shouldly;
using Xunit;
using static SessionInsights.Infrastructure.Tests.TestSupport;

namespace SessionInsights.Infrastructure.Tests;

public sealed class HarnessUpdatesTests
{
    [Theory]
    [InlineData("remote", ".github/plugin/marketplace.json", "./plugins/harness", "0.29.0", null, null)]
    [InlineData("pinned", ".claude-plugin/marketplace.json", "plugins/harness", null, "0.28.0", null)]
    [InlineData("local", "marketplace.json", "./", null, "0.29.0", null)]
    [InlineData("remote", "marketplace.json", "./plugins/harness", "0.29.0-rc.1", "0.29.0",
        "The marketplace did not provide a supported stable plugin version.")]
    [InlineData("remote", "marketplace.json", "../outside", null, "0.29.0",
        "The plugin manifest must be inside its marketplace.")]
    public async Task Copilot_reads_refreshed_metadata_when_browse_omits_version(
        string registration, string catalogPath, string path, string? catalogVersion, string? manifestVersion, string? error)
    {
        var token = TestContext.Current.CancellationToken;
        var fs = new MockFileSystem();
        var options = Options();
        var localRoot = fs.Path.Combine(fs.Path.GetTempPath(), "local-marketplace");
        options.Value.CopilotHome = fs.Path.Combine(fs.Path.GetTempPath(), "custom-copilot");
        var inventory = Substitute.For<IHarnessInventory>();
        inventory.GetAsync(Arg.Any<CancellationToken>(), true).Returns(
            (IReadOnlyList<HarnessInventoryDto>)
            [
                new("claude", "notInstalled", Now, [], null),
                new("copilot", "installed", Now, [new("0.28.0", true, null)], null),
            ]);
        var locator = Substitute.For<IExecutableLocator>();
        locator.Locate("copilot").Returns("/bin/copilot");
        var processes = Substitute.For<IProcessRunner>();
        string? cache = null;
        processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var spec = call.Arg<ProcessSpec>();
            spec.Environment!["COPILOT_HOME"].ShouldBe(options.Value.CopilotHome);
            var commandCache = spec.Environment["COPILOT_CACHE_HOME"];
            if (spec.Arguments.SequenceEqual(new[] { "plugin", "marketplace", "update", "msdc-plugins" }))
            {
                cache = commandCache;
                var root = registration switch
                {
                    "local" => localRoot,
                    "pinned" => fs.Path.Combine(cache, "marketplaces", "_pinned", "owner-repo-pinned"),
                    _ => fs.Path.Combine(cache, "marketplaces", "owner-repo"),
                };
                var catalog = System.Text.Json.JsonSerializer.Serialize(new
                {
                    name = "msdc-plugins",
                    // This is the marketplace's own version and must not be used for the plugin.
                    metadata = new { version = "9.0.0" },
                    plugins = new[] { new { name = "msdc-harness", version = catalogVersion, source = path } },
                });
                fs.AddFile(fs.Path.Combine(root, catalogPath), new MockFileData(catalog));
                if (manifestVersion is not null)
                {
                    fs.AddFile(fs.Path.GetFullPath(fs.Path.Combine(root, path, ".claude-plugin", "plugin.json")),
                        new MockFileData($$"""{"name":"msdc-harness","version":"{{manifestVersion}}"}"""));
                }
                return new ProcessResult(0, "", "");
            }
            commandCache.ShouldBe(cache);
            if (spec.Arguments.SequenceEqual(new[] { "plugin", "marketplace", "browse", "msdc-plugins", "--json" }))
            {
                return new ProcessResult(0, """[{"name":"msdc-harness","description":"Harness","marketplace":"msdc-plugins"}]""", "");
            }
            spec.Arguments.ShouldBe(new[] { "plugin", "marketplace", "list", "--json" });
            registration.ShouldBe("local");
            var list = System.Text.Json.JsonSerializer.Serialize(new[] { new { name = "msdc-plugins", source = "Local: " + localRoot } });
            return new ProcessResult(0, list, "");
        });
        using var updates = new HarnessUpdates(inventory, locator, processes, fs,
            options, TimeProvider.System, NullLogger<HarnessUpdates>.Instance);
        HarnessUpdateDto State() => updates.Get().Single(s => s.Source == "copilot");

        await updates.StartAsync(token);
        try
        {
            await WaitForAsync(() => State().Status is "checked" or "error", token);
            var state = State();
            var expectedVersion = error is null ? catalogVersion ?? manifestVersion : null;
            state.Status.ShouldBe(error is null ? "checked" : "error");
            state.Error.ShouldBe(error);
            state.LatestVersion.ShouldBe(expectedVersion);
            state.Available.ShouldBe(expectedVersion == "0.29.0");
        }
        finally
        {
            await updates.StopAsync(token);
        }
        fs.Directory.Exists(cache.ShouldNotBeNull()).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Queued_checks_preserve_installation_completion_and_outcome(bool installSucceeds, bool checkSucceeds)
    {
        var token = TestContext.Current.CancellationToken;
        var installStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishInstall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCheck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inventory = Substitute.For<IHarnessInventory>();
        var locator = Substitute.For<IExecutableLocator>();
        locator.Locate(Arg.Any<string>()).Returns("/bin/cli");
        var processes = Substitute.For<IProcessRunner>();
        var installed = false;
        var queuedCheck = false;
        var inventoryCalls = 0;
        inventory.GetAsync(Arg.Any<CancellationToken>(), true).Returns(async call =>
        {
            var isQueuedCheck = queuedCheck && Interlocked.Increment(ref inventoryCalls) >= (installSucceeds ? 3 : 2);
            if (isQueuedCheck)
            {
                checkStarted.TrySetResult();
                await finishCheck.Task.WaitAsync(call.Arg<CancellationToken>());
            }
            return (IReadOnlyList<HarnessInventoryDto>)new HarnessInventoryDto[]
            {
                new("claude", "notInstalled", Now, [], null),
                new("copilot", isQueuedCheck && !checkSucceeds ? "error" : "installed", Now,
                    [new(installed ? "1.1.0" : "1.0.0", true, "user")], "Inventory unavailable."),
            };
        });
        processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            var args = call.Arg<ProcessSpec>().Arguments;
            if (args.SequenceEqual(new[] { "plugin", "update", "msdc-harness@msdc-plugins" }))
            {
                installStarted.TrySetResult();
                await finishInstall.Task.WaitAsync(call.Arg<CancellationToken>());
                installed = installSucceeds;
                queuedCheck = true;
                return new ProcessResult(installSucceeds ? 0 : 1, "", "");
            }
            return new ProcessResult(0, """[{"name":"msdc-harness","version":"1.1.0"}]""", "");
        });
        using var updates = new HarnessUpdates(inventory, locator, processes, new MockFileSystem(),
            Options(), TimeProvider.System, NullLogger<HarnessUpdates>.Instance);
        HarnessUpdateDto State() => updates.Get().Single(s => s.Source == "copilot");

        await updates.StartAsync(token);
        try
        {
            await WaitForAsync(() => State().Status == "checked", token);
            State().CompletedAt.ShouldBeNull();
            State().CompletionStatus.ShouldBeNull();
            updates.Start("copilot");
            await installStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            updates.Check();
            finishInstall.SetResult();
            await checkStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), token);

            var completion = State();
            completion.Status.ShouldBe("checking");
            completion.CompletedAt.ShouldNotBeNull();
            completion.CompletionStatus.ShouldBe(installSucceeds ? "succeeded" : "error");
            if (installSucceeds) { completion.CompletionError.ShouldBeNull(); }
            else { completion.CompletionError.ShouldNotBeNull().ShouldContain("Plugin command failed."); }

            finishCheck.SetResult();
            await WaitForAsync(() => State().Status != "checking", token);
            var afterCheck = State();
            afterCheck.Status.ShouldBe(checkSucceeds ? "checked" : "error");
            afterCheck.CompletedAt.ShouldBe(completion.CompletedAt);
            afterCheck.CompletionStatus.ShouldBe(completion.CompletionStatus);
            afterCheck.CompletionError.ShouldBe(completion.CompletionError);

            if (afterCheck.Available)
            {
                var retry = updates.Start("copilot");
                retry.CompletedAt.ShouldBe(completion.CompletedAt);
                retry.CompletionStatus.ShouldBe(completion.CompletionStatus);
                retry.CompletionError.ShouldBe(completion.CompletionError);
            }
        }
        finally
        {
            finishInstall.TrySetResult();
            finishCheck.TrySetResult();
            await updates.StopAsync(token);
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition()) { await Task.Delay(10, timeout.Token); }
    }
}
