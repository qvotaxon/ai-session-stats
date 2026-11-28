using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SessionInsights.Application.Common;
using SessionInsights.Application.Sessions;
using SessionInsights.Domain.Sessions;

namespace SessionInsights.Infrastructure.Sessions;

internal sealed class HarnessInventory(
    IExecutableLocator locator,
    IProcessRunner processes,
    IOptions<SessionInsightsOptions> options,
    TimeProvider clock,
    ILogger<HarnessInventory> logger) : IHarnessInventory
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private IReadOnlyList<HarnessInventoryDto>? snapshot;
    private DateTimeOffset refreshedAt;
    private int renameWarned;

    private static (string Name, string? Marketplace) SplitId(string id)
    {
        var at = id.LastIndexOf('@');
        return at < 0 ? (id, null) : (id[..at], id[(at + 1)..]);
    }

    public async Task<IReadOnlyList<HarnessInventoryDto>> GetAsync(CancellationToken cancellationToken, bool refresh = false)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (refresh || snapshot is null || clock.GetUtcNow() - refreshedAt >= TimeSpan.FromMinutes(1))
            {
                snapshot = await Task.WhenAll(ReadAsync(SessionSource.Claude, cancellationToken), ReadAsync(SessionSource.Copilot, cancellationToken));
                refreshedAt = clock.GetUtcNow();
            }

            return snapshot;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<HarnessInventoryDto> ReadAsync(SessionSource source, CancellationToken cancellationToken)
    {
        var wire = source.ToWire();
        var at = clock.GetUtcNow();
        var executable = locator.Locate(source == SessionSource.Claude ? options.Value.ClaudeCliPath : options.Value.CopilotCliPath);
        if (executable is null)
        {
            return new HarnessInventoryDto(wire, "unavailable", at, [], "CLI executable was not found.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var environment = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(options.Value.ClaudeConfigDir) && source == SessionSource.Claude)
            {
                environment["CLAUDE_CONFIG_DIR"] = options.Value.ClaudeConfigDir;
            }

            if (!string.IsNullOrWhiteSpace(options.Value.CopilotHome) && source == SessionSource.Copilot)
            {
                environment["COPILOT_HOME"] = options.Value.CopilotHome;
            }

            var result = await processes.RunAsync(
                new ProcessSpec(executable, ["plugin", "list", "--json"], AppContext.BaseDirectory, environment), timeout.Token);
            if (result.ExitCode != 0)
            {
                return Failure(wire, at, "Plugin listing failed. Check CLI availability and support for plugin list --json.");
            }

            using var json = JsonDocument.Parse(result.StdOut);
            var list = json.RootElement.ValueKind == JsonValueKind.Array ? json.RootElement : json.RootElement.Get("plugins");
            if (list is not { ValueKind: JsonValueKind.Array } array)
            {
                return Failure(wire, at, "Plugin listing returned an unsupported JSON structure.");
            }

            var installations = new List<HarnessInstallationDto>();
            var renamed = false;
            foreach (var item in array.EnumerateArray())
            {
                JsonElement? plugin = item;
                if (string.IsNullOrWhiteSpace(plugin.Str(source == SessionSource.Claude ? "id" : "name")))
                {
                    return Failure(wire, at, "Plugin listing returned an unsupported plugin entry.");
                }

                var (name, marketplace) = source == SessionSource.Claude
                    ? SplitId(plugin.Str("id")!)
                    : (plugin.Str("name"), plugin.Str("marketplace"));
                if (marketplace == options.Value.HarnessMarketplace && name != options.Value.HarnessPluginName)
                {
                    renamed = true;
                }

                if (name != options.Value.HarnessPluginName || marketplace != options.Value.HarnessMarketplace)
                {
                    continue;
                }

                if (HarnessEvidence.Version(plugin.Str("version")) is not { } version)
                {
                    return Failure(wire, at, "The installed harness has no supported version identifier.");
                }

                var scope = plugin.Str("scope");
                installations.Add(new HarnessInstallationDto(version, plugin.Bool("enabled"),
                    scope is "user" or "project" or "local" or "managed" ? scope : null));
            }

            if (installations.Count == 0 && renamed)
            {
                var message = $"A plugin from marketplace {options.Value.HarnessMarketplace} is installed under a name other than {options.Value.HarnessPluginName}.";
                if (Interlocked.Exchange(ref renameWarned, 1) == 0)
                {
                    logger.LogWarning("Harness inventory for {Source}: {Message}", wire, message);
                }

                return new HarnessInventoryDto(wire, "renamed", at, [], message);
            }

            return new HarnessInventoryDto(wire, installations.Count == 0 ? "notInstalled" : "installed", at, installations, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(wire, at, "Plugin listing timed out.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or Win32Exception or InvalidOperationException)
        {
            return Failure(wire, at, $"Could not read the plugin inventory ({ex.GetType().Name}).");
        }
    }

    private HarnessInventoryDto Failure(string source, DateTimeOffset at, string message)
    {
        logger.LogWarning("Harness inventory for {Source}: {Message}", source, message);
        return new HarnessInventoryDto(source, "error", at, [], message);
    }
}
