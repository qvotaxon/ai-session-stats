using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using SessionInsights.Application.Common;
using SessionInsights.Infrastructure.Logs;
using Shouldly;
using Xunit;

namespace SessionInsights.Infrastructure.Tests;

public sealed class ApplicationLogReaderTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "si-log-reader-tests");

    [Fact]
    public async Task Missing_logs_return_an_empty_result()
    {
        var result = await Reader(new MockFileSystem()).ReadAsync(TestContext.Current.CancellationToken);

        result.FileName.ShouldBeNull();
        result.Content.ShouldBeEmpty();
        result.Truncated.ShouldBeFalse();
    }

    [Fact]
    public async Task Reads_only_the_latest_application_log_and_preserves_multiline_errors()
    {
        var content = "Startup completed\n[ERR] API request failed\nSystem.IO.IOException: test\n   at Example()\n";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [LogPath("session-insights-20261111.log")] = new("old log"),
            [LogPath("session-insights-20261112.log")] = new(content),
            [LogPath("update.log")] = new("unrelated log"),
        });

        var result = await Reader(fs).ReadAsync(TestContext.Current.CancellationToken);

        result.FileName.ShouldBe("session-insights-20261112.log");
        result.Content.ShouldBe(content);
        result.Truncated.ShouldBeFalse();
    }

    [Fact]
    public async Task Caps_the_tail_at_256_KiB_and_omits_a_partial_utf8_line()
    {
        var tail = "\n[ERR] Latest failure\n   at Example()\n";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [LogPath("session-insights-20261112.log")] = new(new string('\u00e9', 150_000) + tail),
        });

        var result = await Reader(fs).ReadAsync(TestContext.Current.CancellationToken);

        result.Truncated.ShouldBeTrue();
        result.Content.ShouldBe(tail[1..]);
        Encoding.UTF8.GetByteCount(result.Content).ShouldBeLessThanOrEqualTo(256 * 1024);
    }

    [Fact]
    public async Task A_file_exactly_at_the_limit_is_not_truncated()
    {
        var content = new string('a', 256 * 1024);
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [LogPath("session-insights-20261112.log")] = new(content),
        });

        var result = await Reader(fs).ReadAsync(TestContext.Current.CancellationToken);

        result.Truncated.ShouldBeFalse();
        result.Content.ShouldBe(content);
    }

    private static string LogPath(string name) => Path.Combine(Root, "logs", name);

    private static ApplicationLogReader Reader(MockFileSystem fs) =>
        new(fs, Options.Create(new SessionInsightsOptions { DataRoot = Root }));
}
