using FFMpegCore;
using KHost.Abstractions.Interactions;
using KHost.Abstractions.Interactions.Requests;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.DataAccess;
using KHost.Domain;
using KHost.IPC.SignalR;
using KHost.ServiceDefaults;
using KHost.Telemetry;
using KHost.UserInterface.Components;
using KHost.UserInterface.Interactions;
using KHost.UserInterface.Middleware;
using KHost.UserInterface.Interactions.Handlers;
using KHost.UserInterface.Services;
using Serilog;
using Serilog.Events;
using KHost.UserInterface.Services.RedirectProviders;

var builder = WebApplication.CreateBuilder(args);

var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logDirectory);

foreach (var staleLog in new DirectoryInfo(logDirectory).GetFiles("*.log")
    .Where(f => f.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-7)))
{
    staleLog.Delete();
}

builder.Host.UseSerilog((_, _, cfg) => cfg
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(logDirectory, ".log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: null,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"));

builder.AddServiceDefaults();

builder.Services.AddTelemetry();
builder.Services.AddDomain();
builder.Services.AddDataAccess();
builder.Services.AddSignalRIPCServer();

// Configure FFmpeg
var ffmpegPath = builder.Configuration["FFmpegPath"];
if (!string.IsNullOrWhiteSpace(ffmpegPath))
    GlobalFFOptions.Configure(opts => opts.BinaryFolder = ffmpegPath);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<IThemeService, ThemeService>();
builder.Services.AddSingleton<IDialogService, DialogService>();
builder.Services.AddSingleton<IStartupRedirectProvider, SetupRedirectProvider>();
builder.Services.AddSingleton<IStartupRedirectProvider, CliStartupRedirectProvider>();

builder.Services.AddSingleton<IInteractionDispatcher, DialogInteractionDispatcher>();
builder.Services.AddSingleton<IInteractionHandler<EditMediaRequest, Media?>, EditMediaDialogHandler>();
builder.Services.AddSingleton<IInteractionHandler<ShowLyricsRequest>, ShowLyricsDialogHandler>();

var app = builder.Build();

// Initialize database with seed data
try
{
    using var scope = app.Services.CreateScope();
    var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
    await initializer.InitializeAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Database initialization failed");
    Log.CloseAndFlush();
    throw;
}

try
{
    await app.Services.GetRequiredService<ISingerQueueService>().InitializeAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Singer queue initialization failed");
    Log.CloseAndFlush();
    throw;
}

try
{
    await app.Services.GetRequiredService<IThemeService>().InitializeAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Theme service initialization failed");
    Log.CloseAndFlush();
    throw;
}

app.MapDefaultEndpoints();
app.MapIPCServer();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.UseStartupRedirect();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
