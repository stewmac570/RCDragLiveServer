using RCDragLiveServer.Security;
using RCDragLiveServer.Services;
using System.Net;
using System.Text;

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

app.MapGet("/", (ILiveRaceStateStore stateStore) =>
{
    var state = stateStore.GetLatest();
    static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    var matchesHtml = new StringBuilder();
    if (state.Matches.Count == 0)
    {
        matchesHtml.Append("<li>No matches available.</li>");
    }
    else
    {
        foreach (var match in state.Matches)
        {
            matchesHtml.Append($"<li>{Encode(match.Driver1)} vs {Encode(match.Driver2)}</li>");
        }
    }

    var html = $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>RC Drag Live</title>
  <style>
    body { font-family: Arial, sans-serif; margin: 0; padding: 16px; line-height: 1.5; }
    main { max-width: 720px; margin: 0 auto; }
    h1 { margin-top: 0; }
  </style>
</head>
<body>
  <main>
    <h1>{{Encode(state.EventName)}}</h1>
    <p><strong>Event Date:</strong> {{Encode(state.EventDate)}}</p>
    <p><strong>Current Round:</strong> {{Encode(state.CurrentRound)}}</p>
    <p><strong>Next Up:</strong> {{Encode(state.NextUp)}}</p>
    <h2>Matches</h2>
    <ul>
      {{matchesHtml}}
    </ul>
  </main>
</body>
</html>
""";

    return Results.Content(html, "text/html; charset=utf-8");
});

app.Run();
