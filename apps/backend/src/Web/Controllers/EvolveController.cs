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
    public sealed record AnalysisRequest(string? SessionId, string? ProjectId, DateTimeOffset? From, DateTimeOffset? To, string? Cli, bool Consent);

    [HttpGet("projects")]
    public async Task<ActionResult<IReadOnlyList<EvolveProject>>> Projects(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListEvolveProjectsQuery(), cancellationToken));

    [HttpPost("analysis")]
    public async Task<ActionResult<SkillAnalysis>> Analyze(
        [FromBody] AnalysisRequest request,
        CancellationToken cancellationToken) =>
        Ok(await mediator.Send(
            new AnalyzeSkillsQuery(request.SessionId, request.ProjectId, request.From, request.To, request.Cli, request.Consent),
            cancellationToken));
}
