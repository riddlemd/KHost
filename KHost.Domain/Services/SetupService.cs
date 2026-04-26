using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

public class SetupService : BaseService, ISetupService
{
    private readonly IUsersService _usersService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IMediaService _mediaService;
    private readonly IVenuesService _venuesService;

    public SetupService(
        ILogger<SetupService> logger,
        IUsersService usersService,
        IVenuesRepository venuesRepository,
        IPasswordHasher passwordHasher,
        IMediaService mediaService,
        IVenuesService venuesService)
        : base(logger)
    {
        _usersService = usersService;
        _passwordHasher = passwordHasher;
        _mediaService = mediaService;
        _venuesService = venuesService;
    }

    public async Task<bool> IsSetupCompleteAsync()
    {
        if (!await _usersService.HasAdminUserAsync())
            return false;

        if (!await _venuesService.HasAnyAsync())
            return false;

        return true;
    }

    public async Task CreateAdminUserAsync(string name, string password)
    {
        var hashedPassword = await _passwordHasher.HashAsync(password);

        var adminUser = new KHostUser
        {
            Name = name,
            IsAdmin = true,
            PasswordHash = hashedPassword
        };

        await _usersService.CreateAsync(adminUser);
        Logger.LogInformation("Admin user '{Name}' created", name);
    }

    public async Task<Guid> CreateDefaultVenueAsync(string name)
    {
        var venue = new Venue
        {
            Name = name,
            Enabled = true
        };

        var createdVenue = await _venuesService.CreateAsync(venue);
        await _venuesService.SelectVenueAsync(createdVenue.Id);

        Logger.LogInformation("Default venue '{Name}' created with ID {VenueId}", name, createdVenue.Id);
        return createdVenue.Id;
    }
}
