using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging.Abstractions;
using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

public class AuthServiceTests
{
    private readonly IUsersRepository _users = Substitute.For<IUsersRepository>();
    private readonly IAuthProvider _provider = Substitute.For<IAuthProvider>();
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly AuthService _service;

    public AuthServiceTests()
    {
        _analytics.StartActivity(Arg.Any<string>()).Returns(Substitute.For<IAnalyticsActivity>());
        _service = new AuthService(
            NullLogger<AuthService>.Instance,
            [_provider],
            _users,
            _analytics);
    }

    [Fact]
    public async Task LoginAsync_ReturnsFailure_WhenUserNotFound()
    {
        _users.FindByNameAsync("unknown").Returns((KHostUser?)null);

        var result = await _service.LoginAsync("unknown", "pass");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task LoginAsync_ReturnsFailure_WhenNoProviderCanHandleUser()
    {
        var user = new KHostUser { Name = "alice" };
        _users.FindByNameAsync("alice").Returns(user);
        _provider.CanHandle(user).Returns(false);

        var result = await _service.LoginAsync("alice", "pass");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task LoginAsync_ReturnsSuccess_WhenProviderAuthenticates()
    {
        var user = new KHostUser { Name = "alice" };
        _users.FindByNameAsync("alice").Returns(user);
        _provider.CanHandle(user).Returns(true);
        _provider.AuthenticateAsync(user, "pass").Returns(AuthResult.Ok(user));

        var result = await _service.LoginAsync("alice", "pass");

        Assert.True(result.Success);
        Assert.Same(user, result.User);
    }

    [Fact]
    public async Task LoginAsync_ReturnsFailure_WhenProviderRejectsPassword()
    {
        var user = new KHostUser { Name = "alice" };
        _users.FindByNameAsync("alice").Returns(user);
        _provider.CanHandle(user).Returns(true);
        _provider.AuthenticateAsync(user, "wrong").Returns(AuthResult.Fail("Invalid name or password"));

        var result = await _service.LoginAsync("alice", "wrong");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }
}
