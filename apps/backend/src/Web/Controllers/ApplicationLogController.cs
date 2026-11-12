using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using SessionInsights.Application.Logs;

namespace SessionInsights.Web.Controllers;

[ApiController]
[Route("api/v1/logs")]
[Produces("application/json")]
public sealed class ApplicationLogController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<ApplicationLogDto>> Get(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetApplicationLogQuery(), cancellationToken));
}
