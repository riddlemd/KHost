namespace KHost.UserInterface.Services.RedirectProviders;

public class CliStartupRedirectProvider : IStartupRedirectProvider
{
    private bool _hasRedirected;
    private string? _page;

    public CliStartupRedirectProvider(IConfiguration configuration)
    {
        _page = configuration["start-page"];
    }

    public Task<bool> ShouldRedirectAsync(HttpContext context) =>
        Task.FromResult(!_hasRedirected && !string.IsNullOrWhiteSpace(_page));

    public Task<string> GetRedirectPathAsync()
    {
        _hasRedirected = true;
        
        return Task.FromResult(_page ?? "/");
    }
}
