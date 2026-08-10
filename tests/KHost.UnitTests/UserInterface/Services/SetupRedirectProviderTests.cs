using KHost.Abstractions.Services;
using KHost.UserInterface.Services.RedirectProviders;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Services;

public class SetupRedirectProviderTests
{
    private readonly IUsersService _usersService = Substitute.For<IUsersService>();
    private readonly IVenuesService _venuesService = Substitute.For<IVenuesService>();

    private SetupRedirectProvider MakeProvider(bool hasAdminUser, bool hasVenue)
    {
        _usersService.HasAdminUserAsync().Returns(hasAdminUser);
        _venuesService.HasAnyAsync().Returns(hasVenue);

        return new SetupRedirectProvider(_usersService, _venuesService, NullLogger<SetupRedirectProvider>.Instance);
    }

    private static HttpContext MakeContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        return context;
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    public async Task ShouldRedirectAsync_RequiresBothAdminUserAndVenue(bool hasAdminUser, bool hasVenue, bool expected)
    {
        var provider = MakeProvider(hasAdminUser, hasVenue);

        var result = await provider.ShouldRedirectAsync(MakeContext("/"));

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("/setup")]
    [InlineData("/setup/venue")]
    [InlineData("/SETUP")]
    public async Task ShouldRedirectAsync_NeverRedirectsAwayFromSetupItself(string path)
    {
        var provider = MakeProvider(hasAdminUser: false, hasVenue: false);

        var result = await provider.ShouldRedirectAsync(MakeContext(path));

        Assert.False(result);
    }

    [Fact]
    public async Task ShouldRedirectAsync_DoesNotQueryServices_ForSetupPaths()
    {
        var provider = MakeProvider(hasAdminUser: false, hasVenue: false);

        await provider.ShouldRedirectAsync(MakeContext("/setup"));

        await _usersService.DidNotReceive().HasAdminUserAsync();
        await _venuesService.DidNotReceive().HasAnyAsync();
    }

    [Fact]
    public async Task ShouldRedirectAsync_TreatsSetupPrefixedPathsAsSetup()
    {
        var provider = MakeProvider(hasAdminUser: false, hasVenue: false);

        // "/setupsomething" shares the prefix, so it is also exempt — documenting current behaviour.
        var result = await provider.ShouldRedirectAsync(MakeContext("/setupsomething"));

        Assert.False(result);
    }

    [Fact]
    public async Task GetRedirectPathAsync_ReturnsSetup()
    {
        var provider = MakeProvider(hasAdminUser: false, hasVenue: false);

        Assert.Equal("/setup", await provider.GetRedirectPathAsync());
    }
}
