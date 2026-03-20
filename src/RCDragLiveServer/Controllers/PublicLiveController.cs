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
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";

        return Ok(stateStore.GetLatest());
    }
}
