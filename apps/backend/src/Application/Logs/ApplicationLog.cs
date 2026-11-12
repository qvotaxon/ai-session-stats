using System.Threading;
using System.Threading.Tasks;
using Mediator;

namespace SessionInsights.Application.Logs;

public sealed record ApplicationLogDto(string? FileName, string Content, bool Truncated);

public interface IApplicationLog
{
    Task<ApplicationLogDto> ReadAsync(CancellationToken cancellationToken);
}

public sealed record GetApplicationLogQuery : IQuery<ApplicationLogDto>;

public sealed class GetApplicationLogQueryHandler(IApplicationLog log)
    : IQueryHandler<GetApplicationLogQuery, ApplicationLogDto>
{
    public async ValueTask<ApplicationLogDto> Handle(GetApplicationLogQuery query, CancellationToken cancellationToken) =>
        await log.ReadAsync(cancellationToken);
}
