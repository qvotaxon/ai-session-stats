using System;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SessionInsights.Application.Common;
using SessionInsights.Infrastructure.Sessions;
using Xunit;
using static SessionInsights.Infrastructure.Tests.TestSupport;

namespace SessionInsights.Infrastructure.Tests;

public sealed class ClaudeAllowanceReaderTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private readonly MockFileSystem fs = new();
    private readonly string path = new MockFileSystem().Path.Combine(DataRoot, "claude-allowance.json");
    private readonly DateTimeOffset now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private ClaudeAllowanceReader Reader() =>
        new(fs, Options(), new FixedTimeProvider(now), NullLogger<ClaudeAllowanceReader>.Instance);

    [Fact]
    public void Missing_data_is_reported_as_unavailable()
    {
        Reader().Get().Status.ShouldBe("unavailable");
    }

    [Fact]
    public void Reads_recent_subscription_windows()
    {
        fs.AddFile(path, new MockFileData(
            """{"updatedAt":"2026-11-18T11:55:00Z","fiveHour":{"usedPercentage":25,"resetsAt":"2026-11-18T15:00:00Z"},"sevenDay":null}"""));

        var allowance = Reader().Get();

        allowance.Status.ShouldBe("available");
        allowance.FiveHour!.UsedPercentage.ShouldBe(25);
        allowance.SevenDay.ShouldBeNull();
    }

    [Fact]
    public void Marks_old_data_as_stale()
    {
        fs.AddFile(path, new MockFileData(
            """{"updatedAt":"2026-11-18T11:00:00Z","fiveHour":{"usedPercentage":25,"resetsAt":null},"sevenDay":null}"""));

        Reader().Get().Status.ShouldBe("stale");
    }

    [Fact]
    public void Rejects_invalid_percentages_without_presenting_them_as_allowance()
    {
        fs.AddFile(path, new MockFileData(
            """{"updatedAt":"2026-11-18T11:55:00Z","fiveHour":{"usedPercentage":101,"resetsAt":null},"sevenDay":null}"""));

        Reader().Get().Status.ShouldBe("error");
    }
}
