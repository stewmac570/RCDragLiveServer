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
builder.Services.AddSingleton<ILiveRaceStateStore, InMemoryLiveRaceStateStore>();

var app = builder.Build();

app.MapControllers();

app.Run();
