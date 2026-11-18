using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using SessionInsights.Application.Sessions;

namespace SessionInsights.Web.Controllers;

[ApiController]
[Route("api/v1/evolve")]
[Produces("application/json")]
public sealed class EvolveController(IMediator mediator) : ControllerBase
{
    [HttpGet("projects")]
    public async Task<ActionResult<IReadOnlyList<EvolveProject>>> Projects(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListEvolveProjectsQuery(), cancellationToken));

    [HttpGet("analysis")]
    public async Task<ActionResult<SkillAnalysis>> Analyze(
        [FromQuery] string? sessionId,
        [FromQuery] string? projectId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new AnalyzeSkillsQuery(sessionId, projectId, from, to), cancellationToken));
}
