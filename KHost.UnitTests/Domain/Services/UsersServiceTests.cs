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
    public async Task ToggleIsRegularAsync_TogglesFlag()
    {
        var user = new KHostUser { Name = "Alice", IsRegular = false };
        var userId = user.Id;

        _repository.ReadAsync(userId)
            .Returns(Task.FromResult((KHostUser?)user));

        await _service.ToggleIsRegularAsync(userId);

        Assert.True(user.IsRegular);
        await _repository.Received(1).UpdateAsync(user);

        _repository.ClearReceivedCalls();
        _repository.ReadAsync(userId)
            .Returns(Task.FromResult((KHostUser?)user));

        await _service.ToggleIsRegularAsync(userId);

        Assert.False(user.IsRegular);
        await _repository.Received(1).UpdateAsync(user);
    }

    [Fact]
    public async Task ToggleIsTipperAsync_TogglesFlag()
    {
        var user = new KHostUser { Name = "Alice", IsTipper = false };
        var userId = user.Id;

        _repository.ReadAsync(userId)
            .Returns(Task.FromResult((KHostUser?)user));

        await _service.ToggleIsTipperAsync(userId);

        Assert.True(user.IsTipper);
        await _repository.Received(1).UpdateAsync(user);
    }

    [Fact]
    public async Task ToggleIsRegularAsync_DoesNothing_WhenUserNotFound()
    {
        var userId = Guid.NewGuid();

        _repository.ReadAsync(userId)
            .Returns(Task.FromResult((KHostUser?)null));

        await _service.ToggleIsRegularAsync(userId);

        await _repository.DidNotReceive().UpdateAsync(Arg.Any<KHostUser>());
    }

    [Fact]
    public async Task ToggleIsTipperAsync_DoesNothing_WhenUserNotFound()
    {
        var userId = Guid.NewGuid();

        _repository.ReadAsync(userId)
            .Returns(Task.FromResult((KHostUser?)null));

        await _service.ToggleIsTipperAsync(userId);

        await _repository.DidNotReceive().UpdateAsync(Arg.Any<KHostUser>());
    }
}
