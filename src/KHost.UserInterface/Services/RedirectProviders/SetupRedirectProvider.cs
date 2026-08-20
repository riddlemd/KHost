using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.UserInterface.Services.RedirectProviders;

public class SetupRedirectProvider : IStartupRedirectProvider
{
    private readonly IUsersService _usersService;
    private readonly IVenuesService _venuesService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SetupRedirectProvider> _logger;

    public SetupRedirectProvider(IUsersService usersService, IVenuesService venuesService, IConfiguration configuration, ILogger<SetupRedirectProvider> logger)
    {
        _usersService = usersService;
        _venuesService = venuesService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> ShouldRedirectAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";

        if (path.StartsWith("/setup", StringComparison.OrdinalIgnoreCase))
            return false;

        _logger.LogDebug("Checking setup status for path {Path}", path);
        // A no-login console needs no admin account to be usable; a venue is enough.
        var requireLogin = _configuration.GetValue<bool?>("Auth:RequireLogin") ?? true;
        var hasAdminUser = !requireLogin || await _usersService.HasAdminUserAsync();
        var hasVenue = await _venuesService.HasAnyAsync();
        var setupComplete = hasAdminUser && hasVenue;
        _logger.LogDebug("Setup complete: {SetupComplete}", setupComplete);
        return !setupComplete;
    }

    public Task<string> GetRedirectPathAsync()
    {
        return Task.FromResult("/setup");
    }
}
