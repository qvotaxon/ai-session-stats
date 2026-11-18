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
