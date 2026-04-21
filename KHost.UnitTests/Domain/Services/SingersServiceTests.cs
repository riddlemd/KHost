using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Domain.Services;

public class SingersServiceTests
{
    private readonly ILogger<SingersService> _logger = Substitute.For<ILogger<SingersService>>();
    private readonly ISingersRepository _repository = Substitute.For<ISingersRepository>();
    private readonly IOptionsMonitor<SingersService.ServiceOptions> _options =
        Substitute.For<IOptionsMonitor<SingersService.ServiceOptions>>();
    private readonly SingersService _service;

    public SingersServiceTests()
    {
        _service = new SingersService(_logger, _options, _repository);
    }

    [Fact]
    public async Task ToggleSingerIsRegularAsync_TogglesFlag()
    {
        var singer = new Singer { Name = "Alice", IsRegular = false };
        var singerId = singer.Id;

        _repository.ReadAsync(singerId)
            .Returns(Task.FromResult((Singer?)singer));

        await _service.ToggleIsRegularAsync(singerId);

        Assert.True(singer.IsRegular);
        await _repository.Received(1).UpdateAsync(singer);

        _repository.ClearReceivedCalls();
        _repository.ReadAsync(singerId)
            .Returns(Task.FromResult((Singer?)singer));

        await _service.ToggleIsRegularAsync(singerId);

        Assert.False(singer.IsRegular);
        await _repository.Received(1).UpdateAsync(singer);
    }

    [Fact]
    public async Task ToggleSingerIsTipperAsync_TogglesFlag()
    {
        var singer = new Singer { Name = "Alice", IsTipper = false };
        var singerId = singer.Id;

        _repository.ReadAsync(singerId)
            .Returns(Task.FromResult((Singer?)singer));

        await _service.ToggleIsTipperAsync(singerId);

        Assert.True(singer.IsTipper);
        await _repository.Received(1).UpdateAsync(singer);
    }

    [Fact]
    public async Task ToggleSingerIsRegularAsync_DoesNothing_WhenSingerNotFound()
    {
        var singerId = Guid.NewGuid();

        _repository.ReadAsync(singerId)
            .Returns(Task.FromResult((Singer?)null));

        await _service.ToggleIsRegularAsync(singerId);

        await _repository.DidNotReceive().UpdateAsync(Arg.Any<Singer>());
    }

    [Fact]
    public async Task ToggleSingerIsTipperAsync_DoesNothing_WhenSingerNotFound()
    {
        var singerId = Guid.NewGuid();

        _repository.ReadAsync(singerId)
            .Returns(Task.FromResult((Singer?)null));

        await _service.ToggleIsTipperAsync(singerId);

        await _repository.DidNotReceive().UpdateAsync(Arg.Any<Singer>());
    }
}
