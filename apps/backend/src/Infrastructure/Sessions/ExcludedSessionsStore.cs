using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SessionInsights.Application.Common;

namespace SessionInsights.Infrastructure.Sessions;

/// <summary>Persisted set of session ids that must never be listed (report forks). Stored in excluded-sessions.json.</summary>
internal sealed class ExcludedSessionsStore : IExcludedSessions
{
    private readonly IFileSystem fs;
    private readonly ILogger<ExcludedSessionsStore> logger;
    private readonly string path;
    private readonly HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock gate = new();

    public ExcludedSessionsStore(IFileSystem fs, IOptions<SessionInsightsOptions> options, ILogger<ExcludedSessionsStore> logger)
    {
        this.fs = fs;
        this.logger = logger;
        path = fs.Path.Combine(options.Value.ResolveDataRoot(), "excluded-sessions.json");
        Load();
    }

    public bool Contains(string sessionId)
    {
        lock (gate)
        {
            return ids.Contains(sessionId);
        }
    }

    public void Add(string sessionId)
    {
        lock (gate)
        {
            if (!ids.Add(sessionId))
            {
                return;
            }

            fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(path)!);
            fs.File.WriteAllText(path, JsonSerializer.Serialize(ids.OrderBy(i => i, StringComparer.Ordinal)));
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

            foreach (var id in JsonSerializer.Deserialize<string[]>(fs.File.ReadAllText(path)) ?? [])
            {
                ids.Add(id);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            logger.LogWarning(ex, "Could not load the excluded sessions file");
        }
    }
}

