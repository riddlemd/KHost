using KHost.DataAccess;
using KHost.Domain;
using KHost.ServiceDefaults;
using KHost.UserInterface.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddDomain();
builder.Services.AddDataAccess();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

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
