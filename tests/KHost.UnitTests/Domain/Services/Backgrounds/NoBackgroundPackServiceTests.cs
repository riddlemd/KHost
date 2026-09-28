using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models.Backgrounds;
using KHost.Abstractions.Services;
using KHost.Domain;
using KHost.Domain.Services.Backgrounds;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.Domain.Services.Backgrounds;

#pragma warning disable CS0618 // the retired service is exactly what this checks
/// <summary>A plugin that still takes the retired background packs in its constructor must load.</summary>
public class NoBackgroundPackServiceTests
{
    [Fact]
    public void AddDomain_StillRegistersTheRetiredService()
    {
        var registered = new ServiceCollection().AddDomain();

        var services = new ServiceCollection();
        foreach (var descriptor in registered.Where(d => d.ServiceType == typeof(IBackgroundPackService)))
            ((IList<ServiceDescriptor>)services).Add(descriptor);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<NoBackgroundPackService>(provider.GetRequiredService<IBackgroundPackService>());
    }

    [Fact]
    public async Task ReadAsync_AnswersThatThereAreNone()
    {
        var pack = await new NoBackgroundPackService().ReadAsync();

        Assert.Empty(pack.Entries);
        Assert.False(pack.HasAny);
        Assert.Equal(BackgroundPackProblem.NoFolderSet, pack.Problem);
    }
}
#pragma warning restore CS0618
