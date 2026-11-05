using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.Extensions.Options;
using SessionInsights.Application.Common;
using SessionInsights.Domain.Sessions;

namespace SessionInsights.Application.Terminals;

/// <summary>The fixed set of terminals a user can pick. Ids are the wire values; clients never send a command.</summary>
public static class TerminalOptions
{
    public const string WtPwsh = "wt-pwsh";
    public const string WtPowerShell = "wt-powershell";
    public const string WtCmd = "wt-cmd";
    public const string Pwsh = "pwsh";
    public const string PowerShell = "powershell";
    public const string Cmd = "cmd";

    public const string Default = WtPwsh;

    public static readonly IReadOnlyList<TerminalOption> All =
    [
        new(WtPwsh, "Windows Terminal + PowerShell 7"),
        new(WtPowerShell, "Windows Terminal + Windows PowerShell"),
        new(WtCmd, "Windows Terminal + Command Prompt"),
        new(Pwsh, "PowerShell 7 window"),
        new(PowerShell, "Windows PowerShell window"),
        new(Cmd, "Command Prompt window"),
    ];

    public static bool IsValid(string? id) => All.Any(o => string.Equals(o.Id, id, StringComparison.Ordinal));
}

public sealed record TerminalOption(string Id, string Label);

public sealed record TerminalOptionDto(string Id, string Label, bool Available);

public sealed record SettingsDto(string Terminal, IReadOnlyList<TerminalOptionDto> Terminals);

public sealed record GetSettingsQuery : IQuery<SettingsDto>;

public sealed class GetSettingsQueryHandler(ITerminalSettings settings, ITerminalLauncher launcher)
    : IQueryHandler<GetSettingsQuery, SettingsDto>
{
    public ValueTask<SettingsDto> Handle(GetSettingsQuery query, CancellationToken cancellationToken) =>
        ValueTask.FromResult(SettingsMapper.Build(settings, launcher));
}

public sealed record UpdateSettingsCommand(string Terminal) : ICommand<SettingsDto>, IValidatable
{
    public string? Validate() => TerminalOptions.IsValid(Terminal) ? null : "terminal is not a supported option.";
}

public sealed class UpdateSettingsCommandHandler(ITerminalSettings settings, ITerminalLauncher launcher)
    : ICommandHandler<UpdateSettingsCommand, SettingsDto>
{
    public ValueTask<SettingsDto> Handle(UpdateSettingsCommand command, CancellationToken cancellationToken)
    {
        settings.SetTerminal(command.Terminal);
        return ValueTask.FromResult(SettingsMapper.Build(settings, launcher));
    }
}

internal static class SettingsMapper
{
    public static SettingsDto Build(ITerminalSettings settings, ITerminalLauncher launcher)
    {
        var available = launcher.Available();
        return new SettingsDto(
            settings.Terminal,
            [.. TerminalOptions.All.Select(o => new TerminalOptionDto(o.Id, o.Label, available.Contains(o.Id)))]);
    }
}

public sealed record ContinueSessionCommand(string SessionId, bool Force) : ICommand<Unit>, IValidatable
{
    public string? Validate() => SessionIdFormat.IsValid(SessionId) ? null : "id must be a GUID.";
}

public sealed class ContinueSessionCommandHandler(
    ISessionIndex index,
    IReportCoordinator reports,
    ITerminalSettings settings,
    ITerminalLauncher launcher,
    IOptions<SessionInsightsOptions> options,
    TimeProvider time) : ICommandHandler<ContinueSessionCommand, Unit>
{
    public const string SessionActiveCode = "session_active";
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(5);

    public async ValueTask<Unit> Handle(ContinueSessionCommand command, CancellationToken cancellationToken)
    {
        var session = await index.FindAsync(command.SessionId, cancellationToken)
            ?? throw new NotFoundException($"Session {command.SessionId} was not found.");

        if (reports.List(session.Id).Any(r => r.IsActive))
        {
            throw new ConflictException("A report is running for this session. Wait for it to finish or cancel it first.");
        }

        if (!command.Force && time.GetUtcNow() - session.LastActivityAt < ActiveWindow)
        {
            throw new ConflictException(
                "This session had activity in the last few minutes and may still be open in another terminal.",
                SessionActiveCode);
        }

        if (string.IsNullOrWhiteSpace(session.Cwd) || !launcher.FolderExists(session.Cwd))
        {
            throw new UnprocessableException("The session's working folder no longer exists.");
        }

        var terminal = launcher.Available().Contains(settings.Terminal) ? settings.Terminal : TerminalOptions.Default;
        if (session.Source == SessionSource.Copilot)
        {
            launcher.Launch(terminal, session.Cwd, options.Value.CopilotCliPath, [$"--resume={session.Id}"]);
        }
        else
        {
            launcher.Launch(terminal, session.Cwd, options.Value.ClaudeCliPath, ["--resume", session.Id]);
        }

        return Unit.Value;
    }
}
