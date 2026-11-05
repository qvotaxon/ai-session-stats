using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SessionInsights.Application.Common;
using SessionInsights.Application.Terminals;
using SessionInsights.Infrastructure.Terminals;
using Xunit;
using static SessionInsights.Infrastructure.Tests.TestSupport;

namespace SessionInsights.Infrastructure.Tests;

public sealed class TerminalCommandBuilderTests
{
    private const string Cwd = @"C:\My Work\repo";
    private const string Id = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    [Fact]
    public void Windows_terminal_passes_folder_and_shell_as_separate_arguments()
    {
        var cmd = TerminalCommandBuilder.Build(TerminalOptions.WtPwsh, @"C:\pwsh.exe", @"C:\wt.exe", Cwd, "copilot", [$"--resume={Id}"]);

        cmd.FileName.ShouldBe(@"C:\wt.exe");
        cmd.UseShellExecute.ShouldBeFalse();
        cmd.Arguments.ShouldBe(["-d", Cwd, @"C:\pwsh.exe", "-NoExit", "-Command", $"& 'copilot' '--resume={Id}'"]);
    }

    [Fact]
    public void Plain_window_runs_the_shell_in_the_folder_via_shell_execute()
    {
        var cmd = TerminalCommandBuilder.Build(TerminalOptions.Cmd, @"C:\cmd.exe", null, Cwd, "claude", ["--resume", Id]);

        cmd.FileName.ShouldBe(@"C:\cmd.exe");
        cmd.WorkingDirectory.ShouldBe(Cwd);
        cmd.UseShellExecute.ShouldBeTrue();
        cmd.Arguments.ShouldBe(["/k", $"claude --resume {Id}"]);
        TerminalCommandBuilder.JoinArguments(cmd.Arguments).ShouldBe($"/k \"claude --resume {Id}\"");
    }

    [Fact]
    public void Command_prompt_preserves_quotes_around_a_cli_path_with_spaces()
    {
        var cli = TerminalCommandBuilder.Build(
            TerminalOptions.Cmd,
            @"C:\cmd.exe",
            null,
            Cwd,
            @"C:\Program Files\Claude\claude.exe",
            ["--resume", Id]);

        cli.Arguments.ShouldBe(["/k", $"\"C:\\Program Files\\Claude\\claude.exe\" --resume {Id}"]);
        TerminalCommandBuilder.JoinCmdArguments(cli.Arguments)
            .ShouldBe($"/k \"\"C:\\Program Files\\Claude\\claude.exe\" --resume {Id}\"");
    }

    [Theory]
    [InlineData("claude & calc")]
    [InlineData("claude\" --x")]
    [InlineData("%PATH%")]
    public void Cli_with_shell_metacharacters_is_rejected(string cli) =>
        Should.Throw<UnprocessableException>(() => TerminalCommandBuilder.Build(TerminalOptions.Pwsh, "pwsh.exe", null, Cwd, cli, ["--resume", Id]));

    [Fact]
    public void Windows_terminal_rejects_semicolon_in_folder() =>
        Should.Throw<UnprocessableException>(() => TerminalCommandBuilder.Build(TerminalOptions.WtCmd, "cmd.exe", "wt.exe", @"C:\a;b", "claude", ["--resume", Id]));
}

public sealed class TerminalSettingsStoreTests
{
    private readonly MockFileSystem fs = new();

    private TerminalSettingsStore Store() => new(fs, Options(), NullLogger<TerminalSettingsStore>.Instance);

    [Fact]
    public void Defaults_then_persists_choice_across_instances()
    {
        Store().Terminal.ShouldBe(TerminalOptions.Default);

        Store().SetTerminal(TerminalOptions.Cmd);

        Store().Terminal.ShouldBe(TerminalOptions.Cmd);
    }

    [Fact]
    public void Corrupt_or_unknown_value_falls_back_to_default()
    {
        fs.AddFile(fs.Path.Combine(Options().Value.ResolveDataRoot(), "settings.json"), new MockFileData("""{"terminal":"calc.exe"}"""));

        Store().Terminal.ShouldBe(TerminalOptions.Default);
    }
}
