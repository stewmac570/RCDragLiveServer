using Microsoft.AspNetCore.Mvc;
using RCDragLiveServer.Services;

namespace RCDragLiveServer.Controllers;

[ApiController]
[Route("api/live")]
public sealed class PublicLiveController(ILiveRaceStateStore stateStore) : ControllerBase
{
    [HttpGet]
    public ActionResult Get()
    {
        ApplyNoCacheHeaders();

        return Ok(stateStore.GetLatest());
    }

    [HttpGet("/health")]
    public ActionResult Health()
    {
        ApplyNoCacheHeaders();

        return Ok(new { status = "healthy" });
    }

    private void ApplyNoCacheHeaders()
    {
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
    }
}
