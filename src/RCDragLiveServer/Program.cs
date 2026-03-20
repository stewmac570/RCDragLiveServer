using RCDragLiveServer.Security;
using RCDragLiveServer.Services;

var builder = WebApplication.CreateBuilder(args);

var apiKey = builder.Configuration["ApiKey"]?.Trim();
if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException("Configuration key 'ApiKey' is required.");
}

var portValue = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(portValue) && int.TryParse(portValue, out var port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddScoped<ApiKeyAuthorizationFilter>();
builder.Services.AddSingleton<ILiveRaceStateStore, InMemoryLiveRaceStateStore>();

var app = builder.Build();

app.MapControllers();

app.Run();
