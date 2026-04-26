namespace KHost.Abstractions.Services;

public interface ISetupService
{
    Task<bool> IsSetupCompleteAsync();
    Task CreateAdminUserAsync(string name, string password);
    Task<Guid> CreateDefaultVenueAsync(string name);
}
