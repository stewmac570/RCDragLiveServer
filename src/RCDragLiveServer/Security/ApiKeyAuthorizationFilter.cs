using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RCDragLiveServer.Security;

public sealed class ApiKeyAuthorizationFilter(IConfiguration configuration) : IAsyncAuthorizationFilter
{
    private const string HeaderName = "X-API-KEY";

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var configuredApiKey = configuration["ApiKey"]?.Trim();
        var providedApiKey = context.HttpContext.Request.Headers[HeaderName].ToString();

        if (string.IsNullOrWhiteSpace(providedApiKey) || !string.Equals(providedApiKey, configuredApiKey, StringComparison.Ordinal))
        {
            context.Result = new JsonResult(new { error = "unauthorized" })
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
        }

        return Task.CompletedTask;
    }
}
