using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using SessionInsights.Application.Terminals;

namespace SessionInsights.Web.Controllers;

public sealed record UpdateSettingsRequest(string? Terminal);

[ApiController]
[Route("api/v1/settings")]
[Produces("application/json")]
public sealed class SettingsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SettingsDto>> Get(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetSettingsQuery(), cancellationToken));

    [HttpPut]
    public async Task<ActionResult<SettingsDto>> Update([FromBody] UpdateSettingsRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new UpdateSettingsCommand(request.Terminal ?? string.Empty), cancellationToken));
}
