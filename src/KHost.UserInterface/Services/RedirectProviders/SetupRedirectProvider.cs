using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.UserInterface.Services.RedirectProviders;

public class SetupRedirectProvider : IStartupRedirectProvider
{
    private readonly IUsersService _usersService;
    private readonly IVenuesService _venuesService;
    private readonly ILogger<SetupRedirectProvider> _logger;

    public SetupRedirectProvider(IUsersService usersService, IVenuesService venuesService, ILogger<SetupRedirectProvider> logger)
    {
        _usersService = usersService;
        _venuesService = venuesService;
        _logger = logger;
    }

    public async Task<bool> ShouldRedirectAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";

        if (path.StartsWith("/setup", StringComparison.OrdinalIgnoreCase))
            return false;

        _logger.LogDebug("Checking setup status for path {Path}", path);
        var hasAdminUser = await _usersService.HasAdminUserAsync();
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
