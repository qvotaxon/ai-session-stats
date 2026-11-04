using System;
using System.IO;

namespace SessionInsights.Application.Common;

public sealed class SessionInsightsOptions
{
    public const string SectionName = "SessionInsights";

    public string? CopilotRoot { get; set; }
    public string? ClaudeRoot { get; set; }
    public string? DataRoot { get; set; }
    public TimeSpan ReportTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public string CopilotCliPath { get; set; } = "copilot";
    public string ClaudeCliPath { get; set; } = "claude";
    public string? StaticFilesPath { get; set; }
    public int Port { get; set; } = 5180;
    public TimeSpan IndexRefreshInterval { get; set; } = TimeSpan.FromSeconds(15);

    public string ResolveCopilotRoot() =>
        !string.IsNullOrWhiteSpace(CopilotRoot) ? CopilotRoot : Path.Combine(UserProfile(), ".copilot", "session-state");

    public string ResolveClaudeRoot() =>
        !string.IsNullOrWhiteSpace(ClaudeRoot) ? ClaudeRoot : Path.Combine(UserProfile(), ".claude", "projects");

    public string ResolveDataRoot() =>
        !string.IsNullOrWhiteSpace(DataRoot)
            ? DataRoot
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SessionInsights");

    private static string UserProfile() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
