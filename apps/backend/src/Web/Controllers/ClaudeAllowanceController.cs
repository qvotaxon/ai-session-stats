using Microsoft.AspNetCore.Mvc;
using SessionInsights.Application.Common;

namespace SessionInsights.Web.Controllers;

[ApiController]
[Route("api/v1/claude/allowance")]
[Produces("application/json")]
public sealed class ClaudeAllowanceController(IClaudeAllowanceReader reader) : ControllerBase
{
    [HttpGet]
    public ActionResult<ClaudeAllowanceDto> Get()
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(reader.Get());
    }
}
