using KHost.Abstractions.Services;
using KHost.DataAccess;
using KHost.Domain;
using KHost.ServiceDefaults;
using KHost.UserInterface.Components;
using Serilog;
using Serilog.Events;

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

builder.Services.AddDomain();
builder.Services.AddDataAccess();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Initialize database with seed data
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
    await initializer.InitializeAsync();
}

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapGet("/api/themes", (IWebHostEnvironment env) =>
{
    var themesPath = Path.Combine(env.WebRootPath, "css", "themes");
    if (!Directory.Exists(themesPath))
        return Results.Ok(new List<string>());

    var themes = Directory.GetFiles(themesPath, "*.css")
        .Select(f => Path.GetFileNameWithoutExtension(f))
        .OrderBy(t => t)
        .ToList();

    return Results.Ok(themes);
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
