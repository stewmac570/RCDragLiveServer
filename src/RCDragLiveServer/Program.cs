using RCDragLiveServer.Services;

var builder = WebApplication.CreateBuilder(args);

var portValue = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(portValue) && int.TryParse(portValue, out var port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSingleton<ILiveRaceStateStore, InMemoryLiveRaceStateStore>();

var app = builder.Build();

app.MapControllers();

app.Run();
