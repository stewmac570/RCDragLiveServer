using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using RCDragLiveServer.Security;
using RCDragLiveServer.Services;

var builder = WebApplication.CreateBuilder(args);

string apiKeySource = "unknown";
if (builder.Configuration is IConfigurationRoot configurationRoot)
{
    foreach (IConfigurationProvider provider in configurationRoot.Providers.Reverse())
    {
        if (provider.TryGet("ApiKey", out var providerValue))
        {
            apiKeySource = provider.ToString() ?? provider.GetType().Name;
            break;
        }
    }
}

var apiKey = builder.Configuration["ApiKey"]?.Trim();
Console.WriteLine("[AUTH] ApiKey config loaded: present=" + (!string.IsNullOrWhiteSpace(apiKey)) + ", length=" + (apiKey?.Length ?? 0) + ", source=" + apiKeySource);
if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException(
        "Configuration key 'ApiKey' is required. Configure it via launchSettings environmentVariables, appsettings.Development.json, appsettings.json, or environment variables.");
}

var portValue = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(portValue) && int.TryParse(portValue, out var port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddScoped<ApiKeyAuthorizationFilter>();
builder.Services.AddSingleton<IDialInStore, InMemoryDialInStore>();
builder.Services.AddSingleton<ILiveRaceStateStore, InMemoryLiveRaceStateStore>();
builder.Services.AddSingleton<IDialInRateLimiter, InMemoryDialInRateLimiter>();

// Render terminates TLS at a load balancer, so without this the only address
// the app ever sees is the proxy's -- which would collapse the per-IP rate
// limiter below into a single global bucket. The proxy's address is dynamic,
// so no fixed known-proxy list is possible.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // AddPolicy, not AddFixedWindowLimiter: the latter builds one bucket shared
    // by every caller, which throttled the whole field to 10 submissions/minute.
    //
    // The limit is deliberately generous. Every phone at the track shares one
    // public IP (the venue 4G router), so this window has to fit a whole field
    // submitting at once; it exists to blunt a flood, not to pace drivers.
    // Per-driver pacing is InMemoryDialInRateLimiter's 5s cooldown.
    options.AddPolicy("dialin-per-ip", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 300,
                QueueLimit = 0
            }));
});

var app = builder.Build();

// Must precede UseRateLimiter so the limiter partitions on the real client IP.
app.UseForwardedHeaders();
app.UseRateLimiter();
app.MapControllers();

app.Run();
