using Microsoft.AspNetCore.Mvc;
using RCDragLiveServer.Models;
using RCDragLiveServer.Security;
using RCDragLiveServer.Services;

namespace RCDragLiveServer.Controllers;

[ApiController]
[Route("api/dialin")]
public sealed class DriverDialInController(IDialInStore dialInStore) : ControllerBase
{
    [HttpPost]
    public IActionResult Post([FromBody] DriverDialInRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.DriverName))
            return BadRequest(new { error = "invalid_payload" });

        var (success, error) = dialInStore.SubmitUpdate(request.DriverName, request.DialIn, request.Pin);

        if (!success)
        {
            return error switch
            {
                "locked"             => StatusCode(423, new { error = "locked" }),
                "invalid_pin"        => Unauthorized(new { error = "invalid_pin" }),
                "invalid_pin_format" => BadRequest(new { error = "invalid_pin_format" }),
                _                    => BadRequest(new { error })
            };
        }

        return Ok(new { status = "updated" });
    }

    [HttpGet]
    [RequireApiKey]
    public IActionResult Get()
    {
        return Ok(dialInStore.GetAll());
    }
}
