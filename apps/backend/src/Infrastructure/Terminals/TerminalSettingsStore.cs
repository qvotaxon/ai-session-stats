using System;
using System.IO;
using System.IO.Abstractions;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SessionInsights.Application.Common;
using SessionInsights.Application.Terminals;

namespace SessionInsights.Infrastructure.Terminals;

/// <summary>Persists the terminal preference in settings.json under the data root. Unknown or unreadable values fall back to the default.</summary>
internal sealed class TerminalSettingsStore : ITerminalSettings
{
    private sealed record SettingsFile(string? Terminal);

    private readonly IFileSystem fs;
    private readonly ILogger<TerminalSettingsStore> logger;
    private readonly string path;
    private readonly Lock gate = new();
    private string terminal = TerminalOptions.Default;

    public TerminalSettingsStore(IFileSystem fs, IOptions<SessionInsightsOptions> options, ILogger<TerminalSettingsStore> logger)
    {
        this.fs = fs;
        this.logger = logger;
        path = fs.Path.Combine(options.Value.ResolveDataRoot(), "settings.json");
        Load();
    }

    public string Terminal
    {
        get
        {
            lock (gate)
            {
                return terminal;
            }
        }
    }

    public void SetTerminal(string id)
    {
        if (!TerminalOptions.IsValid(id))
        {
            throw new ArgumentException("Unsupported terminal.", nameof(id));
        }

        lock (gate)
        {
            terminal = id;
            fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(path)!);
            fs.File.WriteAllText(path, JsonSerializer.Serialize(new SettingsFile(terminal)));
        }
    }

    private void Load()
    {
        try
        {
            if (!fs.File.Exists(path))
            {
                return;
            }

            var file = JsonSerializer.Deserialize<SettingsFile>(fs.File.ReadAllText(path));
            if (TerminalOptions.IsValid(file?.Terminal))
            {
                terminal = file!.Terminal!;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            logger.LogWarning(ex, "Could not load the settings file");
        }
    }
}
