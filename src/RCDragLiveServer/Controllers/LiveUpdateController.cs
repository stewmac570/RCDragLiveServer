using System.Text.Json;
using RCDragLiveServer.Models;
using RCDragLiveServer.Security;
using RCDragLiveServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace RCDragLiveServer.Controllers;

[ApiController]
[Route("api/update")]
[RequireApiKey]
public sealed class LiveUpdateController(ILiveRaceStateStore stateStore) : ControllerBase
{
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [HttpPost]
    public async Task<IActionResult> Post()
    {
        if (Request.Body is null)
        {
            return BadRequest(new { error = "invalid_payload" });
        }

        JsonDocument parsedPayload;
        try
        {
            parsedPayload = await JsonDocument.ParseAsync(Request.Body, cancellationToken: HttpContext.RequestAborted);
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "invalid_payload" });
        }

        using (parsedPayload)
        {
            if (parsedPayload.RootElement.ValueKind != JsonValueKind.Object)
            {
                return BadRequest(new { error = "invalid_payload" });
            }

            if (!parsedPayload.RootElement.TryGetProperty("matches", out var matchesElement) ||
                matchesElement.ValueKind == JsonValueKind.Null)
            {
                return BadRequest(new { error = "invalid_payload" });
            }

            LiveRaceState? payload;
            try
            {
                payload = parsedPayload.RootElement.Deserialize<LiveRaceState>(DeserializeOptions);
            }
            catch (JsonException)
            {
                return BadRequest(new { error = "invalid_payload" });
            }

            if (payload is null)
            {
                return BadRequest(new { error = "invalid_payload" });
            }

            stateStore.SetLatest(payload);
        }

        return Ok(new { status = "updated" });
    }
}
