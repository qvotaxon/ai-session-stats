using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using SessionInsights.Application.Common;
using SessionInsights.Application.Terminals;

namespace SessionInsights.Infrastructure.Terminals;

/// <summary>What to start. <see cref="UseShellExecute"/> processes take <see cref="Arguments"/> as one string; others take the list.</summary>
internal sealed record TerminalCommand(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory, bool UseShellExecute);

internal static class TerminalCommandBuilder
{
    private const string ForbiddenInCli = "\"&|<>^%\r\n";

    /// <summary>Builds the launch command. Never concatenates untrusted text into a shell string: the CLI comes from configuration, the rest are fixed or GUID values.</summary>
    public static TerminalCommand Build(string terminalId, string shellExe, string? wtExe, string workingDirectory, string cli, IReadOnlyList<string> cliArguments)
    {
        if (cli.IndexOfAny(ForbiddenInCli.ToCharArray()) >= 0 || string.IsNullOrWhiteSpace(cli))
        {
            throw new UnprocessableException("The configured CLI path contains unsupported characters.");
        }

        var isCmd = terminalId is TerminalOptions.Cmd or TerminalOptions.WtCmd;
        var line = isCmd ? CmdLine(cli, cliArguments) : PowerShellLine(cli, cliArguments);
        IReadOnlyList<string> shellArgs = isCmd ? ["/k", line] : ["-NoExit", "-Command", line];

        if (terminalId.StartsWith("wt-", StringComparison.Ordinal))
        {
            if (wtExe is null)
            {
                throw new UnprocessableException("Windows Terminal is not available.");
            }

            if (workingDirectory.Contains(';'))
            {
                throw new UnprocessableException("The working folder contains a character Windows Terminal cannot handle.");
            }

            return new TerminalCommand(wtExe, ["-d", workingDirectory, shellExe, .. shellArgs], workingDirectory, UseShellExecute: false);
        }

        return new TerminalCommand(shellExe, shellArgs, workingDirectory, UseShellExecute: true);
    }

    public static string JoinArguments(IReadOnlyList<string> arguments) => string.Join(' ', arguments.Select(QuoteArgument));

    private static string PowerShellLine(string cli, IReadOnlyList<string> args) =>
        "& " + string.Join(' ', new[] { cli }.Concat(args).Select(a => "'" + a.Replace("'", "''", StringComparison.Ordinal) + "'"));

    private static string CmdLine(string cli, IReadOnlyList<string> args) =>
        string.Join(' ', new[] { cli.Any(char.IsWhiteSpace) ? "\"" + cli + "\"" : cli }.Concat(args));

    private static string QuoteArgument(string arg)
    {
        if (arg.Length > 0 && !arg.Any(c => char.IsWhiteSpace(c) || c == '"'))
        {
            return arg;
        }

        var sb = new StringBuilder("\"");
        foreach (var c in arg)
        {
            sb.Append(c == '"' ? "\\\"" : c.ToString());
        }

        return sb.Append('"').ToString();
    }
}

internal sealed class TerminalLauncher(IFileSystem fs, IExecutableLocator locator) : ITerminalLauncher
{
    public IReadOnlyList<string> Available()
    {
        var wt = locator.Locate("wt") is not null;
        var pwsh = locator.Locate("pwsh") is not null;
        var powershell = locator.Locate("powershell") is not null;
        var cmd = locator.Locate("cmd") is not null;

        List<string> result = [];
        AddIf(wt && pwsh, TerminalOptions.WtPwsh);
        AddIf(wt && powershell, TerminalOptions.WtPowerShell);
        AddIf(wt && cmd, TerminalOptions.WtCmd);
        AddIf(pwsh, TerminalOptions.Pwsh);
        AddIf(powershell, TerminalOptions.PowerShell);
        AddIf(cmd, TerminalOptions.Cmd);
        return result;

        void AddIf(bool condition, string id)
        {
            if (condition)
            {
                result.Add(id);
            }
        }
    }

    public bool FolderExists(string path) => fs.Directory.Exists(path);

    public void Launch(string terminalId, string workingDirectory, string cli, IReadOnlyList<string> cliArguments)
    {
        var shellName = terminalId switch
        {
            TerminalOptions.WtPwsh or TerminalOptions.Pwsh => "pwsh",
            TerminalOptions.WtPowerShell or TerminalOptions.PowerShell => "powershell",
            TerminalOptions.WtCmd or TerminalOptions.Cmd => "cmd",
            _ => throw new UnprocessableException("Unsupported terminal."),
        };
        var shellExe = locator.Locate(shellName) ?? throw new UnprocessableException($"{shellName} is not available on this machine.");
        var wtExe = locator.Locate("wt");

        var command = TerminalCommandBuilder.Build(terminalId, shellExe, wtExe, workingDirectory, cli, cliArguments);
        var info = new ProcessStartInfo(command.FileName)
        {
            WorkingDirectory = command.WorkingDirectory,
            UseShellExecute = command.UseShellExecute,
        };
        if (command.UseShellExecute)
        {
            info.Arguments = TerminalCommandBuilder.JoinArguments(command.Arguments);
        }
        else
        {
            foreach (var arg in command.Arguments)
            {
                info.ArgumentList.Add(arg);
            }
        }

        try
        {
            using var process = Process.Start(info);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            throw new UnprocessableException("The terminal could not be started.");
        }
    }
}
