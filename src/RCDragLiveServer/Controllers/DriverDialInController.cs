using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using RCDragLiveServer.Models;
using RCDragLiveServer.Security;
using RCDragLiveServer.Services;

namespace RCDragLiveServer.Controllers;

[ApiController]
[Route("api/dialin")]
public sealed class DriverDialInController(
    IDialInStore dialInStore,
    IDialInRateLimiter rateLimiter,
    ILiveRaceStateStore stateStore) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("dialin-per-ip")]
    public IActionResult Post([FromBody] DriverDialInRequest request)
    {
        if (request is null || request.DriverId <= 0)
            return BadRequest(new { error = "invalid_payload" });

        if (string.IsNullOrWhiteSpace(request.EventId))
            return BadRequest(new { error = "invalid_event" });

        if (!IsValidDialIn(request.DialIn))
            return BadRequest(new { error = "invalid_dialin" });

        if (!IsValidPin(request.Pin))
            return BadRequest(new { error = "invalid_pin_format" });

        var eventKey = stateStore.ResolveEventKey(request.EventId);
        if (!stateStore.EventHasDriver(eventKey, request.DriverId))
            return BadRequest(new { error = "invalid_driver" });

        if (dialInStore.IsLocked(eventKey))
            return StatusCode(423, new { error = "locked" });

        if (!rateLimiter.TryAcquire(eventKey, request.DriverId))
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "rate_limited" });

        var (success, error) = dialInStore.SubmitUpdate(eventKey, request.DriverId, request.DialIn, request.Pin);

        if (!success)
        {
            return error switch
            {
                "locked"             => StatusCode(423, new { error = "locked" }),
                "invalid_pin"        => Unauthorized(new { error = "invalid_pin" }),
                "invalid_pin_format" => BadRequest(new { error = "invalid_pin_format" }),
                "invalid_dialin"     => BadRequest(new { error = "invalid_dialin" }),
                _                    => BadRequest(new { error })
            };
        }

        return Ok(new { status = "updated" });
    }

    [HttpGet]
    [RequireApiKey]
    public IActionResult Get([FromQuery] string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            return BadRequest(new { error = "invalid_event" });

        return Ok(dialInStore.GetAll(stateStore.ResolveEventKey(eventId)));
    }

    private static bool IsValidDialIn(double? dialIn)
    {
        return dialIn.HasValue &&
            dialIn.Value > 0 &&
            !double.IsNaN(dialIn.Value) &&
            !double.IsInfinity(dialIn.Value);
    }

    // The PIN is what stops one driver editing another's dial-in, so it is
    // required rather than optional -- otherwise any caller could claim an
    // unclaimed driver id straight past the web form.
    private static bool IsValidPin(string? pin)
    {
        return pin is not null &&
            pin.Length == 4 &&
            pin.All(char.IsDigit);
    }
}
