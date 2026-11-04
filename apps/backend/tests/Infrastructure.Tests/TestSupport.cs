using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Microsoft.Extensions.Options;
using SessionInsights.Application.Common;

namespace SessionInsights.Infrastructure.Tests;

internal static class TestSupport
{
    public const string CopilotRoot = @"C:\data\copilot";
    public const string ClaudeRoot = @"C:\data\claude";
    public const string DataRoot = @"C:\data\si";

    public static readonly DateTimeOffset Now = new(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);

    public static IOptions<SessionInsightsOptions> Options(TimeSpan? timeout = null) =>
        Microsoft.Extensions.Options.Options.Create(new SessionInsightsOptions
        {
            CopilotRoot = CopilotRoot,
            ClaudeRoot = ClaudeRoot,
            DataRoot = DataRoot,
            ReportTimeout = timeout ?? TimeSpan.FromMinutes(10),
            IndexRefreshInterval = TimeSpan.Zero,
        });

    public static string NewId() => Guid.NewGuid().ToString("D");

    public static void WriteLines(MockFileSystem fs, string path, IEnumerable<string> lines)
    {
        fs.AddDirectory(fs.Path.GetDirectoryName(path)!);
        fs.AddFile(path, new MockFileData(string.Join('\n', lines) + "\n"));
    }

    public static string Ts(int minute, int second = 0) => $"2026-01-10T10:{minute:00}:{second:00}.000Z";

    public static string Json(string type, string timestamp, string data) =>
        $$"""{"type":"{{type}}","timestamp":"{{timestamp}}","data":{{data}}}""";

    public static IReadOnlyList<string> Lines(params string[] lines) => lines.ToList();
}
