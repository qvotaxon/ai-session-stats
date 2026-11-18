using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using SessionInsights.Application.Sessions;

namespace SessionInsights.Web.Controllers;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public sealed class OverviewController(IMediator mediator, IHarnessInventory inventory, IHarnessUpdates updates) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<ActionResult<OverviewDto>> Get(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetOverviewQuery(), cancellationToken));

    [HttpGet("harness/installations")]
    public async Task<ActionResult<IReadOnlyList<HarnessInventoryDto>>> Installations(CancellationToken cancellationToken) =>
        Ok(await inventory.GetAsync(cancellationToken));

    [HttpGet("harness/updates")]
    public ActionResult<IReadOnlyList<HarnessUpdateDto>> Updates() => Ok(updates.Get());

    [HttpPost("harness/updates/check")]
    public IActionResult CheckUpdates()
    {
        updates.Check();
        return Accepted();
    }

    [HttpPost("harness/updates/{source}")]
    public ActionResult<HarnessUpdateDto> Update(string source) => Accepted(updates.Start(source));
}
