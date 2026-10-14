using KHost.Abstractions.Services;
using KHost.UserInterface.Services.RedirectProviders;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Services;

public class SetupRedirectProviderTests
{
    private readonly IVenuesService _venuesService = Substitute.For<IVenuesService>();

    private SetupRedirectProvider MakeProvider(bool hasVenue)
    {
        _venuesService.HasAnyAsync().Returns(hasVenue);

        return new SetupRedirectProvider(_venuesService, NullLogger<SetupRedirectProvider>.Instance);
    }

    private static HttpContext MakeContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        return context;
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ShouldRedirectAsync_RequiresAVenue(bool hasVenue, bool expected)
    {
        var provider = MakeProvider(hasVenue);

        var result = await provider.ShouldRedirectAsync(MakeContext("/"));

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("/setup")]
    [InlineData("/setup/venue")]
    [InlineData("/SETUP")]
    public async Task ShouldRedirectAsync_NeverRedirectsAwayFromSetupItself(string path)
    {
        var provider = MakeProvider(hasVenue: false);

        var result = await provider.ShouldRedirectAsync(MakeContext(path));

        Assert.False(result);
    }

    [Fact]
    public async Task ShouldRedirectAsync_DoesNotQueryServices_ForSetupPaths()
    {
        var provider = MakeProvider(hasVenue: false);

        await provider.ShouldRedirectAsync(MakeContext("/setup"));

        await _venuesService.DidNotReceive().HasAnyAsync();
    }

    [Fact]
    public async Task ShouldRedirectAsync_TreatsSetupPrefixedPathsAsSetup()
    {
        var provider = MakeProvider(hasVenue: false);

        // "/setupsomething" shares the "/setup" prefix, so it is treated as exempt too.
        var result = await provider.ShouldRedirectAsync(MakeContext("/setupsomething"));

        Assert.False(result);
    }

    [Fact]
    public async Task GetRedirectPathAsync_ReturnsSetup()
    {
        var provider = MakeProvider(hasVenue: false);

        Assert.Equal("/setup", await provider.GetRedirectPathAsync());
    }
}
