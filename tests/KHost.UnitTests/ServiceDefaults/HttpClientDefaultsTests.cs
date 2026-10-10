using System.Net;
using KHost.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KHost.UnitTests.ServiceDefaultsTests;

/// <summary>What every IHttpClientFactory client gets, plugins' included: they build none of their own.</summary>
public class HttpClientDefaultsTests
{
    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    private static IServiceProvider Services(Action<IServiceCollection>? more = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        more?.Invoke(builder.Services);
        return builder.Build().Services;
    }

    [Fact]
    public void EveryClient_DecompressesWhatItReceives()
    {
        var handler = Services().GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler("anything");

        var primary = handler;
        while (primary is DelegatingHandler delegating)
            primary = delegating.InnerHandler!;

        Assert.Equal(DecompressionMethods.All, Assert.IsType<SocketsHttpHandler>(primary).AutomaticDecompression);
    }

    /// <summary>A sign-in replayed against a failing server could open a second session, so a POST goes once.</summary>
    [Fact]
    public async Task APostThatFails_IsNotRetried()
    {
        var counting = new CountingHandler();
        var services = Services(s => s.AddHttpClient("probe").ConfigurePrimaryHttpMessageHandler(() => counting));
        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("probe");

        using var response = await client.PostAsync("http://localhost/session/open", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, counting.Calls);
    }
}
