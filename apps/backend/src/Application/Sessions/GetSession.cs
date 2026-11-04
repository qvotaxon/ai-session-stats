using System.Threading;
using System.Threading.Tasks;
using Mediator;
using SessionInsights.Application.Common;
using SessionInsights.Domain.Sessions;

namespace SessionInsights.Application.Sessions;

public sealed record GetSessionQuery(string Id) : IQuery<SessionDetailDto>, IValidatable
{
    public string? Validate() => SessionIdFormat.IsValid(Id) ? null : "id must be a GUID.";
}

public sealed class GetSessionQueryHandler(ISessionIndex index, IReportCoordinator reports)
    : IQueryHandler<GetSessionQuery, SessionDetailDto>
{
    public async ValueTask<SessionDetailDto> Handle(GetSessionQuery query, CancellationToken cancellationToken)
    {
        var session = await index.FindAsync(query.Id, cancellationToken)
            ?? throw new NotFoundException($"Session {query.Id} was not found.");
        return session.ToDetailDto(reports.LatestStatus(session.Id));
    }
}

