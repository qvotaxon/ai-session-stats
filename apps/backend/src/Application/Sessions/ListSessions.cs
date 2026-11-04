using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using SessionInsights.Application.Common;
using SessionInsights.Domain.Sessions;

namespace SessionInsights.Application.Sessions;

public sealed record ListSessionsQuery(
    int Page = 1,
    int PageSize = 25,
    string? Source = null,
    string? Search = null,
    string Sort = "startedAt",
    string Direction = "desc") : IQuery<PagedResult<SessionSummaryDto>>, IValidatable
{
    public const int MaxPageSize = 100;

    private static readonly string[] SortFields = ["startedAt", "totalTokens", "toolCallCount", "durationSeconds"];

    public string? Validate()
    {
        if (Page < 1)
        {
            return "page must be 1 or greater.";
        }

        if (PageSize < 1)
        {
            return "pageSize must be 1 or greater.";
        }

        if (Source is not null && !Source.Equals("copilot", StringComparison.OrdinalIgnoreCase) && !Source.Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            return "source must be 'copilot' or 'claude'.";
        }

        if (!SortFields.Contains(Sort, StringComparer.OrdinalIgnoreCase))
        {
            return "sort must be one of startedAt, totalTokens, toolCallCount, durationSeconds.";
        }

        if (!Direction.Equals("asc", StringComparison.OrdinalIgnoreCase) && !Direction.Equals("desc", StringComparison.OrdinalIgnoreCase))
        {
            return "direction must be 'asc' or 'desc'.";
        }

        return null;
    }
}

public sealed class ListSessionsQueryHandler(ISessionIndex index, IReportCoordinator reports)
    : IQueryHandler<ListSessionsQuery, PagedResult<SessionSummaryDto>>
{
    public async ValueTask<PagedResult<SessionSummaryDto>> Handle(ListSessionsQuery query, CancellationToken cancellationToken)
    {
        IEnumerable<Session> sessions = await index.GetAllAsync(cancellationToken);

        if (query.Source is not null)
        {
            var source = query.Source.Equals("copilot", StringComparison.OrdinalIgnoreCase) ? SessionSource.Copilot : SessionSource.Claude;
            sessions = sessions.Where(s => s.Source == source);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            sessions = sessions.Where(s =>
                s.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (s.Project?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || s.Id.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var descending = query.Direction.Equals("desc", StringComparison.OrdinalIgnoreCase);
        Func<Session, IComparable> key = query.Sort.ToLowerInvariant() switch
        {
            "totaltokens" => s => s.Tokens.Total,
            "toolcallcount" => s => s.ToolCallTotal,
            "durationseconds" => s => s.DurationSeconds,
            _ => s => s.StartedAt,
        };

        var ordered = descending ? sessions.OrderByDescending(key) : sessions.OrderBy(key);
        var all = ordered.ThenBy(s => s.Id, StringComparer.Ordinal).ToList();

        var pageSize = Math.Min(query.PageSize, ListSessionsQuery.MaxPageSize);
        var totalPages = (int)Math.Ceiling(all.Count / (double)pageSize);
        var items = all
            .Skip((query.Page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => s.ToSummaryDto(reports.LatestStatus(s.Id)))
            .ToList();

        return new PagedResult<SessionSummaryDto>(items, query.Page, pageSize, all.Count, totalPages);
    }
}
