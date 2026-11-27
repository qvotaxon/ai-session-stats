using System;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SessionInsights.Application.Common;
using SessionInsights.Infrastructure.Sessions;
using Shouldly;
using Xunit;
using static SessionInsights.Infrastructure.Tests.TestSupport;

namespace SessionInsights.Infrastructure.Tests;

public sealed class CopilotEvolveSessionCleanupTests
{
    private const string Home = @"C:\data\copilot-home";
    private const string StateRoot = Home + @"\session-state";
    private const string DigestText = "secret digest text";

    private readonly MockFileSystem fs = new();
    private readonly ListLogger<CopilotEvolveSessionCleanup> logger = new();
    private readonly IOptions<SessionInsightsOptions> options = Options();

    public CopilotEvolveSessionCleanupTests() => options.Value.CopilotHome = Home;

    private CopilotEvolveSessionCleanup Create(IFileSystem? system = null) => new(system ?? fs, options, logger);

    private string AddSession(string? id = null, string? cwd = null, string? yaml = null, bool withWorkspace = true, string? stateRoot = null)
    {
        id ??= NewId();
        var folder = $@"{stateRoot ?? StateRoot}\{id}";
        fs.AddDirectory(folder);
        fs.AddFile($@"{folder}\events.jsonl", new MockFileData("{}"));
        if (withWorkspace)
        {
            var text = yaml ?? $"id: {id}\ncwd: '{cwd ?? SkillAnalysisActivity.WorkingDirectory}'\nname: {DigestText}\nsummary: {DigestText}\n";
            fs.AddFile($@"{folder}\workspace.yaml", new MockFileData(text));
        }

        return id;
    }

    private bool SessionExists(string id) => fs.Directory.Exists($@"{StateRoot}\{id}");

    private bool SessionIntact(string id) =>
        fs.File.Exists($@"{StateRoot}\{id}\events.jsonl") && fs.File.Exists($@"{StateRoot}\{id}\workspace.yaml");

    private (IFileSystem System, IDirectory Directory) FileSystemThatFailsToDelete(Exception failure)
    {
        var directory = Substitute.For<IDirectory>();
        directory.Exists(Arg.Any<string?>()).Returns(call => fs.Directory.Exists(call.Arg<string?>()));
        directory.When(d => d.Delete(Arg.Any<string>(), Arg.Any<bool>())).Throw(failure);
        var system = Substitute.For<IFileSystem>();
        system.Path.Returns(fs.Path);
        system.File.Returns(fs.File);
        system.FileInfo.Returns(fs.FileInfo);
        system.DirectoryInfo.Returns(fs.DirectoryInfo);
        system.Directory.Returns(directory);
        return (system, directory);
    }

    [Fact]
    public async Task Removes_the_runs_own_session_folder()
    {
        var id = AddSession();
        var sibling = AddSession();
        fs.AddFile($@"{Home}\session-store.db", new MockFileData("db"));

        var outcome = await Create().RemoveAsync(id, false, TestContext.Current.CancellationToken);

        outcome.ShouldBe(EvolveSessionCleanupOutcome.Removed);
        SessionExists(id).ShouldBeFalse();
        SessionIntact(sibling).ShouldBeTrue();
        fs.File.Exists($@"{Home}\session-store.db").ShouldBeTrue();
        var entry = logger.Entries.Single();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Properties["SessionId"].ShouldBe(id);
    }

    [Fact]
    public async Task Removes_a_folder_whose_workspace_has_no_id_key()
    {
        var id = AddSession(yaml: $"cwd: '{SkillAnalysisActivity.WorkingDirectory}'\n");

        var outcome = await Create().RemoveAsync(id, false, TestContext.Current.CancellationToken);

        outcome.ShouldBe(EvolveSessionCleanupOutcome.Removed);
        SessionExists(id).ShouldBeFalse();
    }

    [Fact]
    public async Task Keeps_folder_when_cwd_is_not_evolves()
    {
        var id = AddSession(cwd: @"C:\repo\real-project");

        var outcome = await Create().RemoveAsync(id, false, TestContext.Current.CancellationToken);

        outcome.ShouldBe(EvolveSessionCleanupOutcome.CwdMismatch);
        SessionIntact(id).ShouldBeTrue();
        logger.Entries.Single().Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public async Task Keeps_folder_when_workspace_has_no_cwd()
    {
        var id = NewId();
        AddSession(id, yaml: $"id: {id}\nname: x\n");

        var outcome = await Create().RemoveAsync(id, false, TestContext.Current.CancellationToken);

        outcome.ShouldBe(EvolveSessionCleanupOutcome.CwdMismatch);
        SessionIntact(id).ShouldBeTrue();
    }

    [Fact]
    public async Task Accepts_a_cwd_with_a_trailing_separator_and_different_case()
    {
        var trailing = AddSession(cwd: SkillAnalysisActivity.WorkingDirectory + @"\");
        var upper = AddSession(cwd: SkillAnalysisActivity.WorkingDirectory.ToUpperInvariant());

        (await Create().RemoveAsync(trailing, false, TestContext.Current.CancellationToken)).ShouldBe(EvolveSessionCleanupOutcome.Removed);
        (await Create().RemoveAsync(upper, false, TestContext.Current.CancellationToken)).ShouldBe(EvolveSessionCleanupOutcome.Removed);
    }

    [Fact]
    public async Task Rejects_the_same_leaf_under_another_parent()
    {
        var id = AddSession(cwd: @"C:\somewhere\else\ai-session-stats-evolve");

        var outcome = await Create().RemoveAsync(id, false, TestContext.Current.CancellationToken);

        outcome.ShouldBe(EvolveSessionCleanupOutcome.CwdMismatch);
        SessionIntact(id).ShouldBeTrue();
    }

    [Fact]
    public async Task Keeps_folder_without_workspace_yaml()
    {
        var id = AddSession(withWorkspace: false);

        var outcome = await Create().RemoveAsync(id, false, TestContext.Current.CancellationToken);

        outcome.ShouldBe(EvolveSessionCleanupOutcome.NoWorkspace);
        fs.File.Exists($@"{StateRoot}\{id}\events.jsonl").ShouldBeTrue();
    }

    [Theory]
    [InlineData("cwd: 'unterminated")]
    [InlineData("")]
    [InlineData("just a scalar")]
    [InlineData("- first\n- second\n")]
    public async Task Keeps_folder_with_unparseable_empty_or_non_mapping_yaml(string yaml)
    {
        var id = AddSession(yaml: yaml);

        var outcome = await Create().RemoveAsync(id, false, TestContext.Current.CancellationToken);

        outcome.ShouldBe(EvolveSessionCleanupOutcome.Unreadable);
        SessionIntact(id).ShouldBeTrue();
    }

    [Fact]
    public async Task Keeps_folder_when_it_existed_before_the_run()
    {
        var id = AddSession();

        var outcome = await Create().RemoveAsync(id, true, TestContext.Current.CancellationToken);

        outcome.ShouldBe(EvolveSessionCleanupOutcome.ExistedBeforeRun);
        SessionIntact(id).ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("*")]
    [InlineData(@"..\x")]
    [InlineData(@"..\..\Users")]
    [InlineData("{8f0c5a36-3b57-4e0a-9f7e-0a8a3f6a1b11}")]
    [InlineData("zzzzzzzz-zzzz-zzzz-zzzz-zzzzzzzzzzzz")]
    public async Task Rejects_invalid_ids(string id)
    {
        var sibling = AddSession();

        var outcome = await Create().RemoveAsync(id, false, TestContext.Current.CancellationToken);

        outcome.ShouldBe(EvolveSessionCleanupOutcome.InvalidId);
        Create().Exists(id).ShouldBeFalse();
        SessionIntact(sibling).ShouldBeTrue();
        fs.Directory.Exists(Home).ShouldBeTrue();
        fs.Directory.Exists(StateRoot).ShouldBeTrue();
    }

    [Fact]
    public async Task Keeps_reparse_point_folder()
    {
        var id = AddSession();
        var folder = $@"{StateRoot}\{id}";
        fs.File.SetAttributes(folder, FileAttributes.Directory | FileAttributes.ReparsePoint);

        var outcome = await Create().RemoveAsync(id, false, TestContext.Current.CancellationToken);

        outcome.ShouldBe(EvolveSessionCleanupOutcome.ReparsePoint);
    }

    [Fact]
    public async Task Keeps_folder_with_mismatching_id_key()
    {
        var id = AddSession(yaml: $"id: {NewId()}\ncwd: '{SkillAnalysisActivity.WorkingDirectory}'\n");

        var outcome = await Create().RemoveAsync(id, false, TestContext.Current.CancellationToken);

        outcome.ShouldBe(EvolveSessionCleanupOutcome.IdMismatch);
        SessionIntact(id).ShouldBeTrue();
    }

    [Fact]
    public async Task Refuses_a_root_that_is_not_fully_qualified()
    {
        options.Value.CopilotHome = @"relative\home";
        var id = NewId();
        var folder = $@"relative\home\session-state\{id}";
        fs.AddDirectory(folder);
        fs.AddFile($@"{folder}\workspace.yaml", new MockFileData($"id: {id}\ncwd: '{SkillAnalysisActivity.WorkingDirectory}'\n"));

        var outcome = await Create().RemoveAsync(id, false, TestContext.Current.CancellationToken);

        outcome.ShouldBe(EvolveSessionCleanupOutcome.OutsideRoot);
        Create().Exists(id).ShouldBeFalse();
        fs.File.Exists($@"{folder}\workspace.yaml").ShouldBeTrue();
    }

    [Fact]
    public async Task Reports_not_found_when_the_cli_did_not_create_the_folder()
    {
        var sibling = AddSession();

        var outcome = await Create().RemoveAsync(NewId(), false, TestContext.Current.CancellationToken);

        outcome.ShouldBe(EvolveSessionCleanupOutcome.NotFound);
        SessionIntact(sibling).ShouldBeTrue();
        logger.Entries.Single().Level.ShouldBe(LogLevel.Warning);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task Reports_failure_without_throwing_when_delete_fails(Type failureType)
    {
        var id = AddSession();
        var failure = (Exception)Activator.CreateInstance(failureType, @"denied for C:\Users\name\file")!;
        var (system, directory) = FileSystemThatFailsToDelete(failure);

        var outcome = await Create(system).RemoveAsync(id, false, TestContext.Current.CancellationToken);

        outcome.ShouldBe(EvolveSessionCleanupOutcome.Failed);
        directory.Received(1).Delete($@"{StateRoot}\{id}", true);
        var entry = logger.Entries.Single();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Properties["ExceptionType"].ShouldBe(failureType.Name);
        entry.Message.ShouldNotContain(@"C:\");
        entry.Message.ShouldNotContain("name");
    }

    [Fact]
    public async Task Uses_the_child_copilot_home_not_CopilotRoot()
    {
        options.Value.CopilotRoot = CopilotRoot;
        var id = AddSession();
        AddSession(id, stateRoot: CopilotRoot);

        var outcome = await Create().RemoveAsync(id, false, TestContext.Current.CancellationToken);

        outcome.ShouldBe(EvolveSessionCleanupOutcome.Removed);
        SessionExists(id).ShouldBeFalse();
        fs.File.Exists($@"{CopilotRoot}\{id}\workspace.yaml").ShouldBeTrue();
    }

    [Fact]
    public async Task Never_logs_paths_or_workspace_values()
    {
        var removed = AddSession();
        var mismatch = AddSession(cwd: @"C:\repo\real-project");
        var unreadable = AddSession(yaml: "cwd: 'unterminated");
        var failing = AddSession();
        var (system, _) = FileSystemThatFailsToDelete(new IOException(@"C:\Users\name\secret"));
        var cleanup = Create();

        await cleanup.RemoveAsync(removed, false, TestContext.Current.CancellationToken);
        await cleanup.RemoveAsync(mismatch, false, TestContext.Current.CancellationToken);
        await cleanup.RemoveAsync(unreadable, false, TestContext.Current.CancellationToken);
        await cleanup.RemoveAsync(NewId(), false, TestContext.Current.CancellationToken);
        await cleanup.RemoveAsync(@"..\..\Users\name", false, TestContext.Current.CancellationToken);
        await Create(system).RemoveAsync(failing, false, TestContext.Current.CancellationToken);

        var text = logger.AllText().ToArray();
        text.ShouldNotBeEmpty();
        text.ShouldAllBe(item => !item.Contains(@"C:\") && !item.Contains("copilot-home") && !item.Contains(DigestText) && !item.Contains("name"));
    }

    [Fact]
    public void Exists_is_true_for_a_folder_or_a_file_with_the_id()
    {
        var folder = AddSession();
        var file = NewId();
        fs.AddFile($@"{StateRoot}\{file}", new MockFileData("x"));

        Create().Exists(folder).ShouldBeTrue();
        Create().Exists(file).ShouldBeTrue();
        Create().Exists(NewId()).ShouldBeFalse();
    }

    [Fact]
    public void Exists_rejects_a_non_guid_id_even_when_a_folder_with_that_name_exists()
    {
        fs.AddDirectory($@"{StateRoot}\not-a-guid");

        Create().Exists("not-a-guid").ShouldBeFalse();
    }
}
