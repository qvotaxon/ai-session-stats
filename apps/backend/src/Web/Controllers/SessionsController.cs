using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using SessionInsights.Application.Reports;
using SessionInsights.Application.Sessions;

namespace SessionInsights.Web.Controllers;

[ApiController]
[Route("api/v1/sessions")]
[Produces("application/json")]
public sealed class SessionsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<SessionSummaryDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? source = null,
        [FromQuery] string? search = null,
        [FromQuery] string sort = "startedAt",
        [FromQuery] string direction = "desc",
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new ListSessionsQuery(page, pageSize, source, search, sort, direction), cancellationToken));

    [HttpGet("{id}")]
    public async Task<ActionResult<SessionDetailDto>> Get(string id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetSessionQuery(id), cancellationToken));

    [HttpPost("{id}/reports")]
    public async Task<IActionResult> StartReport(string id, CancellationToken cancellationToken) =>
        Accepted(await mediator.Send(new StartReportCommand(id), cancellationToken));

    [HttpGet("{id}/reports")]
    public async Task<ActionResult<IReadOnlyList<ReportRunDto>>> ListReports(string id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListReportsQuery(id), cancellationToken));

    [HttpGet("{id}/reports/{runId}")]
    public async Task<ActionResult<ReportRunDto>> GetReport(string id, string runId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetReportQuery(id, runId), cancellationToken));

    [HttpGet("{id}/reports/{runId}/content")]
    [Produces("text/markdown")]
    public async Task<IActionResult> GetReportContent(string id, string runId, CancellationToken cancellationToken)
    {
        var content = await mediator.Send(new GetReportContentQuery(id, runId), cancellationToken);
        return Content(content.Markdown, "text/markdown; charset=utf-8");
    }

    [HttpGet("{id}/reports/{runId}/download")]
    [Produces("text/markdown")]
    public async Task<IActionResult> DownloadReport(string id, string runId, CancellationToken cancellationToken)
    {
        var content = await mediator.Send(new GetReportContentQuery(id, runId), cancellationToken);
        return File(System.Text.Encoding.UTF8.GetBytes(content.Markdown), "text/markdown; charset=utf-8", content.FileName);
    }

    [HttpPost("{id}/reports/{runId}/cancel")]
    public async Task<IActionResult> CancelReport(string id, string runId, CancellationToken cancellationToken) =>
        Accepted(await mediator.Send(new CancelReportCommand(id, runId), cancellationToken));
}
