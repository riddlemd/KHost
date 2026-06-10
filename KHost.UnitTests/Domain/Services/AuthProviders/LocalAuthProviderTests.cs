using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.AuthProviders;

namespace KHost.UnitTests.Domain.Services.AuthProviders;

public class LocalAuthProviderTests
{
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly LocalAuthProvider _provider;

    public LocalAuthProviderTests()
    {
        _provider = new LocalAuthProvider(_hasher);
    }

    [Fact]
    public void CanHandle_ReturnsTrue_WhenUserHasPasswordHash()
    {
        var user = new KHostUser { Name = "test", PasswordHash = "v2:abc:def" };

        Assert.True(_provider.CanHandle(user));
    }

    [Fact]
    public void CanHandle_ReturnsFalse_WhenUserHasNoPasswordHash()
    {
        var user = new KHostUser { Name = "test", PasswordHash = null };

        Assert.False(_provider.CanHandle(user));
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsSuccess_WhenPasswordVerifies()
    {
        var user = new KHostUser { Name = "test", PasswordHash = "v2:abc:def" };
        _hasher.VerifyAsync("secret", user.PasswordHash).Returns(true);

        var result = await _provider.AuthenticateAsync(user, "secret");

        Assert.True(result.Success);
        Assert.Same(user, result.User);
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsFailure_WhenPasswordDoesNotVerify()
    {
        var user = new KHostUser { Name = "test", PasswordHash = "v2:abc:def" };
        _hasher.VerifyAsync("wrong", user.PasswordHash).Returns(false);

        var result = await _provider.AuthenticateAsync(user, "wrong");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }
}
