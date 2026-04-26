using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.UserInterface.Services.RedirectProviders;

public class SetupRedirectProvider : IStartupRedirectProvider
{
    private readonly ISetupService _setupService;
    private readonly ILogger<SetupRedirectProvider> _logger;

    public SetupRedirectProvider(ISetupService setupService, ILogger<SetupRedirectProvider> logger)
    {
        _setupService = setupService;
        _logger = logger;
    }

    public async Task<bool> ShouldRedirectAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";

        if (path.StartsWith("/setup", StringComparison.OrdinalIgnoreCase))
            return false;

        _logger.LogDebug("Checking setup status for path {Path}", path);
        var setupComplete = await _setupService.IsSetupCompleteAsync();
        _logger.LogDebug("Setup complete: {SetupComplete}", setupComplete);
        return !setupComplete;
    }

    public Task<string> GetRedirectPathAsync()
    {
        return Task.FromResult("/setup");
    }
}
