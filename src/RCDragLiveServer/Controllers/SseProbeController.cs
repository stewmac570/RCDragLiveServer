using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace RCDragLiveServer.Controllers;

/// <summary>
/// Diagnostic only. Answers one question: does Render's proxy stream a
/// text/event-stream response through to the client, or does it buffer the whole
/// thing and deliver it at the end? Everything about pushing live race updates to
/// phones instead of polling depends on that answer, and it can only be observed
/// in production -- locally there is no proxy in the way.
///
/// Emits five ticks a second apart, each stamped server-side. A client that
/// receives them a second apart is being streamed; a client that receives all
/// five at once after five seconds is being buffered.
///
/// Delete once the transport decision is made.
/// </summary>
[ApiController]
[Route("sse-probe")]
public sealed class SseProbeController : ControllerBase
{
    private const int TickCount = 5;
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    [HttpGet]
    public async Task Get(CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-store, max-age=0";
        Response.Headers.Pragma = "no-cache";

        // The nginx-family hint that tells a reverse proxy not to buffer this
        // response. Render terminates at such a proxy, so if streaming works at
        // all, it most likely works because of this.
        Response.Headers["X-Accel-Buffering"] = "no";

        // Kestrel will otherwise hold small writes back waiting for more.
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        for (int tick = 1; tick <= TickCount && !cancellationToken.IsCancellationRequested; tick++)
        {
            var sentAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            await Response.WriteAsync($"event: tick\n", cancellationToken);
            await Response.WriteAsync($"data: {{\"tick\":{tick},\"sentAtUnixMs\":{sentAt}}}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);

            if (tick < TickCount)
            {
                try
                {
                    await Task.Delay(TickInterval, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }
}
