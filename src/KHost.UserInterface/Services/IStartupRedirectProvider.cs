namespace KHost.UserInterface.Services;

public interface IStartupRedirectProvider
{
    Task<bool> ShouldRedirectAsync(HttpContext context);
    Task<string> GetRedirectPathAsync();
}
