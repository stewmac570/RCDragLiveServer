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
    ILiveRaceStateStore stateStore,
    ILiveUpdateBroadcaster broadcaster) : ControllerBase
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

        if (!rateLimiter.TryAcquire(eventKey, request.DriverId))
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "rate_limited" });

        var (success, error) = dialInStore.SubmitUpdate(eventKey, request.DriverId, request.DialIn, request.Pin);

        if (!success)
        {
            return error switch
            {
                "invalid_pin"        => Unauthorized(new { error = "invalid_pin" }),
                "invalid_pin_format" => BadRequest(new { error = "invalid_pin_format" }),
                "invalid_dialin"     => BadRequest(new { error = "invalid_dialin" }),
                _                    => BadRequest(new { error })
            };
        }

        // Everyone watching this event should see the new dial-in, not just the
        // driver who entered it.
        broadcaster.Publish(eventKey);

        // Saved regardless. "pending" tells the driver their time lands in the next
        // round rather than the one already generated.
        return Ok(new { status = "updated", pending = dialInStore.IsLocked(eventKey) });
    }

    [HttpPost("login")]
    [EnableRateLimiting("dialin-per-ip")]
    public IActionResult Login([FromBody] DriverDialInRequest request)
    {
        if (request is null || request.DriverId <= 0)
            return BadRequest(new { error = "invalid_payload" });

        if (string.IsNullOrWhiteSpace(request.EventId))
            return BadRequest(new { error = "invalid_event" });

        if (!IsValidPin(request.Pin))
            return BadRequest(new { error = "invalid_pin_format" });

        var eventKey = stateStore.ResolveEventKey(request.EventId);
        if (!stateStore.EventHasDriver(eventKey, request.DriverId))
            return BadRequest(new { error = "invalid_driver" });

        var (ok, error) = dialInStore.VerifyPin(eventKey, request.DriverId, request.Pin);
        if (!ok)
        {
            return error == "invalid_pin"
                ? Unauthorized(new { error = "invalid_pin" })
                : BadRequest(new { error });
        }

        dialInStore.GetAll(eventKey).TryGetValue(request.DriverId, out var currentDialIn);

        return Ok(new
        {
            status = "ok",
            dialIn = currentDialIn,
            pending = dialInStore.IsLocked(eventKey)
        });
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
