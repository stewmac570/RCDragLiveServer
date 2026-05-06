using Microsoft.AspNetCore.Mvc;
using RCDragLiveServer.Security;
using RCDragLiveServer.Services;

namespace RCDragLiveServer.Controllers;

[ApiController]
[Route("api/reset")]
[RequireApiKey]
public sealed class EventResetController(ILiveRaceStateStore stateStore) : ControllerBase
{
    public sealed class ResetRequest
    {
        public string? EventId { get; set; }
        public string? EventName { get; set; }
    }

    [HttpPost]
    public IActionResult Post([FromBody] ResetRequest request)
    {
        if (request is null ||
            (string.IsNullOrWhiteSpace(request.EventId) && string.IsNullOrWhiteSpace(request.EventName)))
        {
            return BadRequest(new { error = "eventId or eventName is required" });
        }

        stateStore.ClearEvent(request.EventId ?? string.Empty, request.EventName);
        return Ok(new { status = "cleared" });
    }
}
