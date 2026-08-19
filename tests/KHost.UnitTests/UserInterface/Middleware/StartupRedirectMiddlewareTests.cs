using KHost.UserInterface.Middleware;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Middleware;

public class StartupRedirectMiddlewareTests
{
    private static IStartupRedirectProvider MakeProvider(bool shouldRedirect, string redirectPath = "/setup")
    {
        var provider = Substitute.For<IStartupRedirectProvider>();
        provider.ShouldRedirectAsync(Arg.Any<HttpContext>()).Returns(shouldRedirect);
        provider.GetRedirectPathAsync().Returns(redirectPath);
        return provider;
    }

    private static (HttpContext Context, Func<bool> NextCalled, StartupRedirectMiddleware Middleware) Arrange(
        string path,
        params IStartupRedirectProvider[] providers)
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var context = new DefaultHttpContext();
        context.Request.Path = path;

        var middleware = new StartupRedirectMiddleware(
            next, providers, NullLogger<StartupRedirectMiddleware>.Instance);

        return (context, () => nextCalled, middleware);
    }

    [Theory]
    [InlineData("/_framework/blazor.server.js")]
    [InlineData("/_blazor")]
    [InlineData("/css/site.css")]
    [InlineData("/js/app.js")]
    [InlineData("/images/logo.png")]
    [InlineData("/fonts/icons.woff2")]
    [InlineData("/favicon.ico")]
    [InlineData("/_content/pkg/x.css")]
    [InlineData("/api/health")]
    public async Task InvokeAsync_SkipsRedirect_ForStaticContentPrefixes(string path)
    {
        var provider = MakeProvider(shouldRedirect: true);
        var (context, nextCalled, middleware) = Arrange(path, provider);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled());
        await provider.DidNotReceive().ShouldRedirectAsync(Arg.Any<HttpContext>());
    }

    [Theory]
    [InlineData("/anything.css")]
    [InlineData("/deep/path/style.scss")]
    [InlineData("/x.js")]
    [InlineData("/a.png")]
    [InlineData("/a.jpg")]
    [InlineData("/a.svg")]
    [InlineData("/a.woff2")]
    [InlineData("/a.map")]
    public async Task InvokeAsync_SkipsRedirect_ForStaticFileExtensions(string path)
    {
        var provider = MakeProvider(shouldRedirect: true);
        var (context, nextCalled, middleware) = Arrange(path, provider);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled());
        await provider.DidNotReceive().ShouldRedirectAsync(Arg.Any<HttpContext>());
    }

    [Theory]
    [InlineData("/media/abc123/stream.m3u8")]
    [InlineData("/media/abc123/seg_00001.ts")]
    [InlineData("/ipc/screen")]
    [InlineData("/IPC/Screen")]
    public async Task InvokeAsync_SkipsRedirect_ForMachineFacingPaths(string path)
    {
        var provider = MakeProvider(shouldRedirect: true);
        var (context, nextCalled, middleware) = Arrange(path, provider);

        await middleware.InvokeAsync(context);

        // A Cast receiver cannot follow a 302 to /setup; it renders the HTML as a broken stream.
        Assert.True(nextCalled());
        Assert.NotEqual(StatusCodes.Status302Found, context.Response.StatusCode);
        await provider.DidNotReceive().ShouldRedirectAsync(Arg.Any<HttpContext>());
    }

    [Fact]
    public async Task InvokeAsync_StaticContentMatchIsCaseInsensitive()
    {
        var provider = MakeProvider(shouldRedirect: true);
        var (context, nextCalled, middleware) = Arrange("/CSS/Site.CSS", provider);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled());
    }

    [Fact]
    public async Task InvokeAsync_RedirectsAndSkipsNext_WhenProviderSaysSo()
    {
        var provider = MakeProvider(shouldRedirect: true, redirectPath: "/setup");
        var (context, nextCalled, middleware) = Arrange("/", provider);

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled());
        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/setup", context.Response.Headers.Location);
    }

    [Fact]
    public async Task InvokeAsync_CallsNext_WhenNoProviderWantsARedirect()
    {
        var provider = MakeProvider(shouldRedirect: false);
        var (context, nextCalled, middleware) = Arrange("/", provider);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled());
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_CallsNext_WhenThereAreNoProviders()
    {
        var (context, nextCalled, middleware) = Arrange("/");

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled());
    }

    [Fact]
    public async Task InvokeAsync_UsesFirstMatchingProvider_AndSkipsLaterOnes()
    {
        var first = MakeProvider(shouldRedirect: true, redirectPath: "/first");
        var second = MakeProvider(shouldRedirect: true, redirectPath: "/second");
        var (context, _, middleware) = Arrange("/", first, second);

        await middleware.InvokeAsync(context);

        Assert.Equal("/first", context.Response.Headers.Location);
        await second.DidNotReceive().ShouldRedirectAsync(Arg.Any<HttpContext>());
    }

    [Fact]
    public async Task InvokeAsync_FallsThroughToLaterProvider_WhenEarlierOneDeclines()
    {
        var first = MakeProvider(shouldRedirect: false);
        var second = MakeProvider(shouldRedirect: true, redirectPath: "/second");
        var (context, nextCalled, middleware) = Arrange("/", first, second);

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled());
        Assert.Equal("/second", context.Response.Headers.Location);
    }

    [Fact]
    public async Task InvokeAsync_Rethrows_WhenAProviderThrows()
    {
        var provider = Substitute.For<IStartupRedirectProvider>();
        provider.ShouldRedirectAsync(Arg.Any<HttpContext>())
            .Returns<bool>(_ => throw new InvalidOperationException("boom"));

        var (context, nextCalled, middleware) = Arrange("/", provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));
        Assert.False(nextCalled());
    }
}
