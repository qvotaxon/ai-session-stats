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
public sealed record SkillOpportunity(string Id, string Title, string Outline, string Instructions, int Occurrences, int SessionCount, IReadOnlyList<SkillEvidence> Evidence);
public sealed record SkillAnalysis(int SessionsScanned, int RequestsAnalyzed, bool Sampled, IReadOnlyList<SkillOpportunity> Opportunities);

public enum SkillAnalysisScope
{
    Session,
    Project,
}

public interface ISkillAnalyzer
{
    Task<SkillAnalysis> AnalyzeAsync(IReadOnlyList<Session> sessions, DateTimeOffset? from, DateTimeOffset? to, SessionSource cli, SkillAnalysisScope scope, CancellationToken cancellationToken);
}

public sealed record ListEvolveProjectsQuery : IQuery<IReadOnlyList<EvolveProject>>;

public sealed record AnalyzeSkillsQuery(string? SessionId, string? ProjectId, DateTimeOffset? From, DateTimeOffset? To, string? Cli, bool Consent)
    : IQuery<SkillAnalysis>, IValidatable
{
    public string? Validate()
    {
        if (!Consent)
        {
            return "Consent is required before selected prompts can be sent to the local CLI.";
        }

        if (Cli is not ("claude" or "copilot"))
        {
            return "Select the claude or copilot CLI.";
        }

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

public sealed class EvolveQueryHandler(ISessionIndex index, ISkillAnalyzer analyzer)
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

        return await analyzer.AnalyzeAsync(sessions, query.From, query.To,
            query.Cli == "claude" ? SessionSource.Claude : SessionSource.Copilot,
            query.SessionId is null ? SkillAnalysisScope.Project : SkillAnalysisScope.Session, cancellationToken);
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
