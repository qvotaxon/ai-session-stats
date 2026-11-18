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
    private const string Marketplace = "msdc-plugins";
    private const string Plugin = "msdc-harness";
    private static readonly string[] CopilotCatalogPaths =
        ["marketplace.json", ".plugin/marketplace.json", ".github/plugin/marketplace.json", ".claude-plugin/marketplace.json"];
    private static readonly string[] CopilotManifestPaths =
        [".plugin/plugin.json", "plugin.json", ".github/plugin/plugin.json", ".claude-plugin/plugin.json"];

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

            state = state with { Status = "updating", Error = null };
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
                            states[source] = states[source] with { Status = "checking", Available = false, Error = null };
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
        string? copilotCache = null;
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

            if (source == "copilot")
            {
                // A fresh cache lets Copilot resolve the registered source and ref without guessing its checkout name.
                copilotCache = fs.Directory.CreateTempSubdirectory("session-insights-copilot-").FullName;
            }
            await CommandAsync(source, ["plugin", "marketplace", "update", Marketplace], token, copilotCache);
            var latest = await LatestAsync(source, copilotCache, token);
            if (install)
            {
                await CommandAsync(source, ["plugin", "update", $"{Plugin}@{Marketplace}"], token, copilotCache);
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
                    CompletionStatus = install ? "succeeded" : states[source].CompletionStatus,
                    CompletionError = install ? null : states[source].CompletionError,
                    Error = null,
                };
            }
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            Fail(source, install, "Plugin maintenance timed out. Check CLI authentication and retry.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or Win32Exception or InvalidOperationException or ArgumentException)
        {
            // Never expose CLI output, marketplace credentials, or local paths.
            var message = ex is InvalidOperationException ? ex.Message : $"Could not read the marketplace ({ex.GetType().Name}).";
            Fail(source, install, message);
        }
        finally
        {
            if (copilotCache is not null)
            {
                try { fs.Directory.Delete(copilotCache, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning("Could not remove the temporary Copilot marketplace cache ({ErrorType}).", ex.GetType().Name);
                }
            }
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
                CompletionStatus = install ? "error" : states[source].CompletionStatus,
                CompletionError = install ? message : states[source].CompletionError,
                Error = message,
            };
        }
    }

    private async Task<string> CommandAsync(string source, string[] arguments, CancellationToken token, string? copilotCache = null)
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
        if (copilotCache is not null)
        {
            environment["COPILOT_CACHE_HOME"] = copilotCache;
        }
        var result = await processes.RunAsync(new ProcessSpec(executable, arguments, AppContext.BaseDirectory, environment), token);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("Plugin command failed. Check CLI authentication and marketplace access, then retry.");
        }
        return result.StdOut;
    }

    private async Task<string> LatestAsync(string source, string? copilotCache, CancellationToken token)
    {
        string? version;
        if (copilotCache is not null)
        {
            var browse = await CommandAsync(source, ["plugin", "marketplace", "browse", Marketplace, "--json"], token, copilotCache);
            using var json = JsonDocument.Parse(browse);
            // Copilot's browse projection can omit version even when the catalog declares it.
            version = HarnessEntry(json.RootElement).Str("version") ?? await CopilotCatalogVersionAsync(copilotCache, token);
        }
        else
        {
            var home = new[] { options.Value.ClaudeConfigDir, Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") }
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? fs.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            using var known = JsonDocument.Parse(await fs.File.ReadAllTextAsync(fs.Path.Combine(home, "plugins", "known_marketplaces.json"), token));
            var marketplaceRoot = known.RootElement.Get(Marketplace).Str("installLocation")
                ?? throw new InvalidOperationException("The registered Claude marketplace has no local catalog.");
            using var catalog = await ReadJsonAsync(fs.Path.Combine(marketplaceRoot, ".claude-plugin", "marketplace.json"), token);
            var plugin = HarnessEntry(catalog.RootElement);
            version = plugin.Str("version");
            if (version is null && plugin.Str("source") is { } path && path.StartsWith("./", StringComparison.Ordinal))
            {
                var root = fs.Path.GetFullPath(marketplaceRoot);
                var directory = fs.Path.GetFullPath(fs.Path.Combine(root, path));
                if (!directory.StartsWith(root + fs.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("The plugin manifest must be inside its marketplace.");
                }
                using var manifest = await ReadJsonAsync(fs.Path.Combine(directory, ".claude-plugin", "plugin.json"), token);
                version = ((JsonElement?)manifest.RootElement).Str("version");
            }
        }
        if (version is null || !Version.TryParse(version, out var stable) || stable.Build < 0 || stable.Revision >= 0)
        {
            throw new InvalidOperationException("The marketplace did not provide a supported stable plugin version.");
        }
        return version;
    }

    private static JsonElement? HarnessEntry(JsonElement catalog)
    {
        var plugins = catalog.ValueKind == JsonValueKind.Array ? catalog : catalog.Get("plugins");
        if (plugins is not { ValueKind: JsonValueKind.Array } array)
        {
            throw new InvalidOperationException("Marketplace returned an unsupported catalog structure.");
        }
        var matches = array.EnumerateArray().Where(p => ((JsonElement?)p).Str("name") == Plugin).ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException("The marketplace must contain exactly one MSDC harness entry.");
        }
        return matches[0];
    }

    private async Task<string?> CopilotCatalogVersionAsync(string cache, CancellationToken token)
    {
        var marketplaces = fs.Path.Combine(cache, "marketplaces");
        var pinned = fs.Path.Combine(marketplaces, "_pinned");
        var roots = CheckoutDirectories(marketplaces).Where(path => path != pinned).Concat(CheckoutDirectories(pinned)).ToArray();
        if (roots.Length == 0)
        {
            // Directory marketplaces are read in place and do not produce a checkout.
            var list = await CommandAsync("copilot", ["plugin", "marketplace", "list", "--json"], token, cache);
            using var known = JsonDocument.Parse(list);
            if (known.RootElement.ValueKind == JsonValueKind.Array)
            {
                const string localPrefix = "Local: ";
                roots = known.RootElement.EnumerateArray().Select(entry => (JsonElement?)entry)
                    .Where(entry => entry.Str("name") == Marketplace)
                    .Select(entry => entry.Str("source"))
                    .OfType<string>()
                    .Where(source => source.StartsWith(localPrefix, StringComparison.Ordinal))
                    .Select(source => source[localPrefix.Length..])
                    .Where(fs.Path.IsPathFullyQualified)
                    .ToArray();
            }
        }
        var catalogs = roots.Select(root => (Root: root, Path: FindMarketplaceFile(root, CopilotCatalogPaths)))
            .Where(catalog => catalog.Path is not null).ToArray();
        if (catalogs.Length != 1)
        {
            throw new InvalidOperationException("Could not locate the refreshed Copilot marketplace catalog.");
        }
        var (marketplaceRoot, catalogPath) = catalogs[0];
        using var catalog = await ReadJsonAsync(catalogPath!, token);
        if (((JsonElement?)catalog.RootElement).Str("name") != Marketplace)
        {
            throw new InvalidOperationException("The refreshed Copilot catalog did not match the registered marketplace.");
        }
        var plugin = HarnessEntry(catalog.RootElement);
        if (plugin.Str("version") is { } version) { return version; }
        if (plugin.Str("source") is not { Length: > 0 } path || fs.Path.IsPathRooted(path)) { return null; }
        if (FindMarketplaceFile(marketplaceRoot, CopilotManifestPaths.Select(p => fs.Path.Combine(path, p))) is not { } manifestPath)
        {
            return null;
        }
        using var manifest = await ReadJsonAsync(manifestPath, token);
        return ((JsonElement?)manifest.RootElement).Str("version");
    }

    private IEnumerable<string> CheckoutDirectories(string path) =>
        fs.Directory.Exists(path) ? fs.Directory.EnumerateDirectories(path) : [];

    private string? FindMarketplaceFile(string root, IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            var file = fs.Path.GetFullPath(fs.Path.Combine(root, candidate));
            var relative = fs.Path.GetRelativePath(root, file);
            var parts = relative.Split(fs.Path.DirectorySeparatorChar);
            if (fs.Path.IsPathRooted(relative) || parts[0] == "..")
            {
                throw new InvalidOperationException("The plugin manifest must be inside its marketplace.");
            }
            if (!fs.File.Exists(file)) { continue; }
            var current = root;
            foreach (var part in parts)
            {
                current = fs.Path.Combine(current, part);
                if ((fs.File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException("The marketplace catalog and plugin manifest cannot use symbolic links.");
                }
            }
            return file;
        }
        return null;
    }

    private async Task<JsonDocument> ReadJsonAsync(string path, CancellationToken token) =>
        JsonDocument.Parse(await fs.File.ReadAllTextAsync(path, token));

    private static int Compare(string installed, string latest)
    {
        var core = installed.Split('+')[0].Split('-')[0];
        if (!Version.TryParse(core, out var version))
        {
            throw new InvalidOperationException("The installed plugin has an unsupported version.");
        }
        var comparison = version.CompareTo(Version.Parse(latest));
        return comparison == 0 && installed.Split('+')[0].Contains('-', StringComparison.Ordinal) ? -1 : comparison;
    }
}
