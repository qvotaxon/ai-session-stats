using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SessionInsights.Application.Common;
using SessionInsights.Application.Sessions;

namespace SessionInsights.Infrastructure.Sessions;

internal sealed class HarnessUpdates(
    IHarnessInventory inventory,
    IExecutableLocator locator,
    IProcessRunner processes,
    IFileSystem fs,
    IOptions<SessionInsightsOptions> options,
    TimeProvider clock,
    ILogger<HarnessUpdates> logger) : BackgroundService, IHarnessUpdates
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, HarnessUpdateDto> states = new()
    {
        ["claude"] = new("claude", "pending", null, false, null, null, null),
        ["copilot"] = new("copilot", "pending", null, false, null, null, null),
    };
    private readonly Channel<(string Source, bool Install)> queue =
        Channel.CreateUnbounded<(string, bool)>(new UnboundedChannelOptions { SingleReader = true });
    private bool checkQueued;

    public IReadOnlyList<HarnessUpdateDto> Get()
    {
        lock (gate) { return [.. states.Values]; }
    }

    public HarnessUpdateDto Start(string source)
    {
        lock (gate)
        {
            if (!states.TryGetValue(source, out var state))
            {
                throw new RequestValidationException("Source must be claude or copilot.");
            }

            if (!state.Available || state.Status == "updating")
            {
                throw new ConflictException("No plugin update is available, or an update is already running.");
            }

            state = state with { Status = "updating", Error = null, CompletedAt = null };
            states[source] = state;
            queue.Writer.TryWrite((source, true));
            return state;
        }
    }

    public void Check()
    {
        lock (gate)
        {
            if (checkQueued) { return; }
            checkQueued = true;
            queue.Writer.TryWrite(("", false));
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(ScheduleAsync(stoppingToken), WorkAsync(stoppingToken));

    private async Task ScheduleAsync(CancellationToken token)
    {
        Check();
        using var timer = new PeriodicTimer(TimeSpan.FromHours(4), clock);
        while (await timer.WaitForNextTickAsync(token)) { Check(); }
    }

    private async Task WorkAsync(CancellationToken token)
    {
        await foreach (var work in queue.Reader.ReadAllAsync(token))
        {
            if (work.Install)
            {
                await RunAsync(work.Source, true, token);
            }
            else
            {
                try
                {
                    foreach (var source in new[] { "claude", "copilot" })
                    {
                        lock (gate)
                        {
                            if (states[source].Status == "updating") { continue; }
                            states[source] = states[source] with { Status = "checking", Available = false };
                        }
                        await RunAsync(source, false, token);
                    }
                }
                finally
                {
                    lock (gate) { checkQueued = false; }
                }
            }
        }
    }

    private async Task RunAsync(string source, bool install, CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        try
        {
            var before = (await inventory.GetAsync(token, refresh: true)).Single(i => i.Source == source);
            if (before.Status != "installed")
            {
                throw new InvalidOperationException(before.Error ?? "The MSDC harness is not installed in this CLI scope.");
            }
            // These CLIs run from the application directory: never update a project/local/managed installation.
            if (before.Installations.Any(i => i.Scope is not (null or "user")))
            {
                throw new InvalidOperationException("Automatic updates only support user-scoped plugin installations.");
            }

            await CommandAsync(source, ["plugin", "marketplace", "update", "msdc-plugins"], token);
            var latest = await LatestAsync(source, token);
            if (install)
            {
                await CommandAsync(source, ["plugin", "update", "msdc-harness@msdc-plugins"], token);
                var after = (await inventory.GetAsync(token, refresh: true)).Single(i => i.Source == source);
                if (after.Status != "installed" || after.Installations.Count == 0 ||
                    after.Installations.Any(i => Compare(i.Version, latest) < 0))
                {
                    throw new InvalidOperationException("The update command finished, but the new installed version could not be verified.");
                }
            }
            var available = !install && before.Installations.Any(i => Compare(i.Version, latest) < 0);
            lock (gate)
            {
                states[source] = states[source] with
                {
                    Status = install ? "succeeded" : "checked",
                    LatestVersion = latest,
                    Available = available,
                    CheckedAt = clock.GetUtcNow(),
                    CompletedAt = install ? clock.GetUtcNow() : states[source].CompletedAt,
                    Error = null,
                };
            }
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            Fail(source, install, "Plugin maintenance timed out. Check CLI authentication and retry.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or Win32Exception or InvalidOperationException)
        {
            // Never expose CLI output, marketplace credentials, or local paths.
            var message = ex is InvalidOperationException ? ex.Message : $"Could not read the marketplace ({ex.GetType().Name}).";
            Fail(source, install, message);
        }
    }

    private void Fail(string source, bool install, string message)
    {
        logger.LogWarning("Harness {Operation} for {Source}: {Message}", install ? "update" : "check", source, message);
        lock (gate)
        {
            states[source] = states[source] with
            {
                Status = "error",
                Available = install && states[source].Available,
                CheckedAt = clock.GetUtcNow(),
                CompletedAt = install ? clock.GetUtcNow() : states[source].CompletedAt,
                Error = message,
            };
        }
    }

    private async Task<string> CommandAsync(string source, string[] arguments, CancellationToken token)
    {
        var executable = locator.Locate(source == "claude" ? options.Value.ClaudeCliPath : options.Value.CopilotCliPath)
            ?? throw new InvalidOperationException("CLI executable was not found.");
        var environment = new Dictionary<string, string>();
        if (source == "claude" && !string.IsNullOrWhiteSpace(options.Value.ClaudeConfigDir))
        {
            environment["CLAUDE_CONFIG_DIR"] = options.Value.ClaudeConfigDir;
        }
        if (source == "copilot" && !string.IsNullOrWhiteSpace(options.Value.CopilotHome))
        {
            environment["COPILOT_HOME"] = options.Value.CopilotHome;
        }
        var result = await processes.RunAsync(new ProcessSpec(executable, arguments, AppContext.BaseDirectory, environment), token);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("Plugin command failed. Check CLI authentication and marketplace access, then retry.");
        }
        return result.StdOut;
    }

    private async Task<string> LatestAsync(string source, CancellationToken token)
    {
        string catalog;
        string? marketplaceRoot = null;
        if (source == "copilot")
        {
            catalog = await CommandAsync(source, ["plugin", "marketplace", "browse", "msdc-plugins", "--json"], token);
        }
        else
        {
            var home = options.Value.ClaudeConfigDir ?? Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")
                ?? fs.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            using var known = JsonDocument.Parse(await fs.File.ReadAllTextAsync(fs.Path.Combine(home, "plugins", "known_marketplaces.json"), token));
            marketplaceRoot = known.RootElement.Get("msdc-plugins").Str("installLocation")
                ?? throw new InvalidOperationException("The registered Claude marketplace has no local catalog.");
            catalog = await fs.File.ReadAllTextAsync(fs.Path.Combine(marketplaceRoot, ".claude-plugin", "marketplace.json"), token);
        }
        using var json = JsonDocument.Parse(catalog);
        var plugins = json.RootElement.ValueKind == JsonValueKind.Array ? json.RootElement : json.RootElement.Get("plugins");
        if (plugins is not { ValueKind: JsonValueKind.Array } array)
        {
            throw new InvalidOperationException("Marketplace returned an unsupported catalog structure.");
        }
        var matches = array.EnumerateArray().Where(p => ((JsonElement?)p).Str("name") == "msdc-harness").ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException("The marketplace must contain exactly one MSDC harness entry.");
        }
        JsonElement? plugin = matches[0];
        var version = plugin.Str("version");
        if (version is null && marketplaceRoot is not null && plugin.Str("source") is { } path && path.StartsWith("./", StringComparison.Ordinal))
        {
            var root = fs.Path.GetFullPath(marketplaceRoot);
            var directory = fs.Path.GetFullPath(fs.Path.Combine(root, path));
            if (!directory.StartsWith(root + fs.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The plugin manifest must be inside its marketplace.");
            }
            using var manifest = JsonDocument.Parse(await fs.File.ReadAllTextAsync(fs.Path.Combine(directory, ".claude-plugin", "plugin.json"), token));
            version = ((JsonElement?)manifest.RootElement).Str("version");
        }
        if (version is null || !Version.TryParse(version, out var stable) || stable.Build < 0 || stable.Revision >= 0)
        {
            throw new InvalidOperationException("The marketplace did not provide a supported stable plugin version.");
        }
        return version;
    }

    private static int Compare(string installed, string latest)
    {
        var core = installed.Split('+')[0].Split('-')[0];
        if (!Version.TryParse(core, out var version))
        {
            throw new InvalidOperationException("The installed plugin has an unsupported version.");
        }
        var comparison = version.CompareTo(Version.Parse(latest));
        return comparison == 0 && installed.Contains('-', StringComparison.Ordinal) ? -1 : comparison;
    }
}
