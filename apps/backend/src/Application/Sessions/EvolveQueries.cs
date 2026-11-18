using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using SessionInsights.Application.Common;
using SessionInsights.Domain.Sessions;

namespace SessionInsights.Application.Sessions;

public sealed record EvolveProject(string Id, string Name, int SessionCount);
public sealed record SkillEvidence(string SessionId, string Title, int Occurrences);
public sealed record SkillOpportunity(string Id, string Title, string Outline, int Occurrences, int SessionCount, IReadOnlyList<SkillEvidence> Evidence);
public sealed record SkillAnalysis(int SessionsScanned, IReadOnlyList<SkillOpportunity> Opportunities);

public sealed record ListEvolveProjectsQuery : IQuery<IReadOnlyList<EvolveProject>>;

public sealed record AnalyzeSkillsQuery(string? SessionId, string? ProjectId, DateTimeOffset? From, DateTimeOffset? To)
    : IQuery<SkillAnalysis>, IValidatable
{
    public string? Validate()
    {
        if ((SessionId is null) == (ProjectId is null))
        {
            return "Select exactly one session or project.";
        }

        if (SessionId is not null && !SessionIdFormat.IsValid(SessionId))
        {
            return "sessionId must be a GUID.";
        }

        if (ProjectId is not null && (ProjectId.Length != 64 || !ProjectId.All(Uri.IsHexDigit)))
        {
            return "projectId must be an identifier from the Evolve projects list.";
        }

        return From > To ? "The start time must be on or before the end time." : null;
    }
}

public sealed class EvolveQueryHandler(ISessionIndex index)
    : IQueryHandler<ListEvolveProjectsQuery, IReadOnlyList<EvolveProject>>,
      IQueryHandler<AnalyzeSkillsQuery, SkillAnalysis>
{
    public async ValueTask<IReadOnlyList<EvolveProject>> Handle(ListEvolveProjectsQuery query, CancellationToken cancellationToken) =>
        [.. (await index.GetAllAsync(cancellationToken))
            .Select(s => (Session: s, ProjectId: ProjectId(s)))
            .Where(s => s.ProjectId is not null)
            .GroupBy(s => s.ProjectId!, StringComparer.Ordinal)
            .Select(g => new EvolveProject(g.Key, g.First().Session.Project ?? "Unnamed project", g.Count()))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Id, StringComparer.Ordinal)];

    public async ValueTask<SkillAnalysis> Handle(AnalyzeSkillsQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<Session> sessions;
        if (query.SessionId is { } id)
        {
            var session = await index.FindAsync(id, cancellationToken)
                ?? throw new NotFoundException($"Session {id} was not found.");
            sessions = [session];
        }
        else
        {
            sessions = (await index.GetAllAsync(cancellationToken))
                .Where(s => string.Equals(ProjectId(s), query.ProjectId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (sessions.Count == 0)
            {
                throw new NotFoundException("The selected project was not found.");
            }
        }

        var matches = new Dictionary<string, List<SkillEvidence>>(StringComparer.Ordinal);
        foreach (var session in sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var group in session.SkillSignals
                .Where(s => (query.From is null || s.At >= query.From) && (query.To is null || s.At <= query.To))
                .GroupBy(s => s.Workflow, StringComparer.Ordinal))
            {
                if (!matches.TryGetValue(group.Key, out var evidence))
                {
                    matches[group.Key] = evidence = [];
                }

                evidence.Add(new SkillEvidence(session.Id, session.Title, group.Count()));
            }
        }

        return new SkillAnalysis(sessions.Count,
            [.. SkillWorkflow.Known
                .Where(w => matches.ContainsKey(w.Id))
                .Select(w =>
                {
                    var evidence = matches[w.Id];
                    return new SkillOpportunity(w.Id, w.Title, w.Outline, evidence.Sum(e => e.Occurrences), evidence.Count,
                        [.. evidence.OrderByDescending(e => e.Occurrences).ThenBy(e => e.SessionId, StringComparer.Ordinal).Take(5)]);
                })
                .Where(o => o.Occurrences >= 2)
                .OrderByDescending(o => o.SessionCount)
                .ThenByDescending(o => o.Occurrences)
                .ThenBy(o => o.Id, StringComparer.Ordinal)]);
    }

    private static string? ProjectId(Session session)
    {
        if (string.IsNullOrWhiteSpace(session.Cwd))
        {
            return null;
        }

        // Use the recorded directory as identity without exposing it through the API.
        var cwd = session.Cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (OperatingSystem.IsWindows())
        {
            cwd = cwd.Replace('/', '\\').ToUpperInvariant();
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(cwd)));
    }
}
