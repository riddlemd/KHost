using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Domain.Services;

public class UsersServiceTests
{
    private readonly ILogger<UsersService> _logger = Substitute.For<ILogger<UsersService>>();
    private readonly IUsersRepository _repository = Substitute.For<IUsersRepository>();
    private readonly IOptionsMonitor<UsersService.ServiceOptions> _options =
        Substitute.For<IOptionsMonitor<UsersService.ServiceOptions>>();
    private readonly UsersService _service;

    public UsersServiceTests()
    {
        _service = new UsersService(_logger, _options, _repository);
    }

    [Fact]
    public async Task HasAdminUserAsync_ReturnsTrueWhenAdminExists()
    {
        _repository.HasAdminUserAsync().Returns(Task.FromResult(true));

        var result = await _service.HasAdminUserAsync();

        Assert.True(result);
        await _repository.Received(1).HasAdminUserAsync();
    }

    [Fact]
    public async Task HasAdminUserAsync_ReturnsFalseWhenNoAdminExists()
    {
        _repository.HasAdminUserAsync().Returns(Task.FromResult(false));

        var result = await _service.HasAdminUserAsync();

        Assert.False(result);
        await _repository.Received(1).HasAdminUserAsync();
    }
}
