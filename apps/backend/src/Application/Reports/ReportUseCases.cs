using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using SessionInsights.Application.Common;
using SessionInsights.Application.Sessions;
using SessionInsights.Domain.Reports;
using SessionInsights.Domain.Sessions;

namespace SessionInsights.Application.Reports;

public sealed record ReportRunDto(
    string Id,
    string SessionId,
    int Version,
    string Status,
    string Cli,
    bool Forked,
    string? ForkedSessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    long DurationMs,
    int? ExitCode,
    string? Error);

public sealed record ReportContentDto(string Markdown, string FileName);

public static class ReportMappings
{
    public static ReportRunDto ToDto(this ReportRun r) => new(
        r.Id, r.SessionId, r.Version, r.Status.ToWire(), r.Cli.ToWire(), r.Forked, r.ForkedSessionId,
        r.StartedAt, r.FinishedAt, r.DurationMs, r.ExitCode, r.Error);
}

public sealed record StartReportCommand(string SessionId) : ICommand<ReportRunDto>, IValidatable
{
    public string? Validate() => SessionIdFormat.IsValid(SessionId) ? null : "id must be a GUID.";
}

public sealed class StartReportCommandHandler(IReportCoordinator coordinator) : ICommandHandler<StartReportCommand, ReportRunDto>
{
    public async ValueTask<ReportRunDto> Handle(StartReportCommand command, CancellationToken cancellationToken) =>
        (await coordinator.StartAsync(command.SessionId, cancellationToken)).ToDto();
}

public sealed record ListReportsQuery(string SessionId) : IQuery<IReadOnlyList<ReportRunDto>>, IValidatable
{
    public string? Validate() => SessionIdFormat.IsValid(SessionId) ? null : "id must be a GUID.";
}

public sealed class ListReportsQueryHandler(ISessionIndex index, IReportCoordinator coordinator)
    : IQueryHandler<ListReportsQuery, IReadOnlyList<ReportRunDto>>
{
    public async ValueTask<IReadOnlyList<ReportRunDto>> Handle(ListReportsQuery query, CancellationToken cancellationToken)
    {
        _ = await index.FindAsync(query.SessionId, cancellationToken)
            ?? throw new NotFoundException($"Session {query.SessionId} was not found.");
        return [.. coordinator.List(query.SessionId).Select(r => r.ToDto())];
    }
}

public sealed record GetReportQuery(string SessionId, string RunId) : IQuery<ReportRunDto>, IValidatable
{
    public string? Validate() =>
        !SessionIdFormat.IsValid(SessionId) ? "id must be a GUID." : !Guid.TryParse(RunId, out _) ? "runId must be a GUID." : null;
}

public sealed class GetReportQueryHandler(ISessionIndex index, IReportCoordinator coordinator) : IQueryHandler<GetReportQuery, ReportRunDto>
{
    public async ValueTask<ReportRunDto> Handle(GetReportQuery query, CancellationToken cancellationToken)
    {
        _ = await index.FindAsync(query.SessionId, cancellationToken)
            ?? throw new NotFoundException($"Session {query.SessionId} was not found.");
        return (coordinator.Find(query.SessionId, query.RunId) ?? throw new NotFoundException($"Report run {query.RunId} was not found.")).ToDto();
    }
}

public sealed record GetReportContentQuery(string SessionId, string RunId) : IQuery<ReportContentDto>, IValidatable
{
    public string? Validate() =>
        !SessionIdFormat.IsValid(SessionId) ? "id must be a GUID." : !Guid.TryParse(RunId, out _) ? "runId must be a GUID." : null;
}

public sealed class GetReportContentQueryHandler(ISessionIndex index, IReportCoordinator coordinator)
    : IQueryHandler<GetReportContentQuery, ReportContentDto>
{
    public async ValueTask<ReportContentDto> Handle(GetReportContentQuery query, CancellationToken cancellationToken)
    {
        _ = await index.FindAsync(query.SessionId, cancellationToken)
            ?? throw new NotFoundException($"Session {query.SessionId} was not found.");
        var run = coordinator.Find(query.SessionId, query.RunId) ?? throw new NotFoundException($"Report run {query.RunId} was not found.");
        var markdown = run.Status == ReportStatus.Succeeded ? await coordinator.ReadContentAsync(run, cancellationToken) : null;
        return markdown is null
            ? throw new NotFoundException("The report has no content yet.")
            : new ReportContentDto(markdown, $"session-{run.SessionId}-v{run.Version}.md");
    }
}

public sealed record CancelReportCommand(string SessionId, string RunId) : ICommand<ReportRunDto>, IValidatable
{
    public string? Validate() =>
        !SessionIdFormat.IsValid(SessionId) ? "id must be a GUID." : !Guid.TryParse(RunId, out _) ? "runId must be a GUID." : null;
}

public sealed class CancelReportCommandHandler(ISessionIndex index, IReportCoordinator coordinator)
    : ICommandHandler<CancelReportCommand, ReportRunDto>
{
    public async ValueTask<ReportRunDto> Handle(CancelReportCommand command, CancellationToken cancellationToken)
    {
        _ = await index.FindAsync(command.SessionId, cancellationToken)
            ?? throw new NotFoundException($"Session {command.SessionId} was not found.");
        return coordinator.Cancel(command.SessionId, command.RunId).ToDto();
    }
}

