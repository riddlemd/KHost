using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Domain.Services.Messaging;
using KHost.Domain.Services.Visualisations;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services.Visualisations;

public class VisualisationPlaylistServiceTests
{
    private readonly IVisualisationPlaylistRepository _repository = Substitute.For<IVisualisationPlaylistRepository>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly QueuedRandom _random = new();
    private readonly VisualisationPlaylist _playlist = new()
    {
        Name = "Night",
        Entries = [Entry("A"), Entry("B"), Entry("C")],
    };

    public VisualisationPlaylistServiceTests()
    {
        _repository.ReadWithEntriesAsync(_playlist.Id).Returns(_ => _playlist);
        _repository.ReadAsync(_playlist.Id).Returns(_ => _playlist);
    }

    private VisualisationPlaylistService Service() => new(NullLogger<VisualisationPlaylistService>.Instance, _repository, _broker, _random);

    private static VisualisationEntry Entry(string preset) => new() { PresetName = preset };

    private async Task<List<string>> PickAsync(VisualisationPlaylistService service, int songs)
    {
        var picked = new List<string>();
        for (var i = 0; i < songs; i++) picked.Add((await service.SelectNextAsync(_playlist.Id))!.PresetName);
        return picked;
    }

    [Fact]
    public async Task SelectNextAsync_InOrder_TakesEachEntryInTurnAndWraps()
        => Assert.Equal(["A", "B", "C", "A"], await PickAsync(Service(), 4));

    [Fact]
    public async Task SelectNextAsync_Shuffled_NeverPicksTheSameEntryTwiceRunning()
    {
        _playlist.Shuffle = true;
        // Asking for the top of the range each time: with the last pick taken out, the top is
        // always a different entry than a plain draw over all three would give.
        foreach (var value in new[] { 2, 1, 1, 1 }) _random.Values.Enqueue(value);

        Assert.Equal(["C", "B", "C", "B"], await PickAsync(Service(), 4));
    }

    [Fact]
    public async Task SelectNextAsync_Shuffled_OneEntry_KeepsPickingIt()
    {
        _playlist.Shuffle = true;
        _playlist.Entries = [Entry("Only")];

        Assert.Equal(["Only", "Only"], await PickAsync(Service(), 2));
    }

    [Fact]
    public async Task SelectNextAsync_AnEmptyPlaylist_IsNull()
    {
        _playlist.Entries = [];

        Assert.Null(await Service().SelectNextAsync(_playlist.Id));
    }

    [Fact]
    public async Task SelectNextAsync_NoSuchPlaylist_IsNull()
        => Assert.Null(await Service().SelectNextAsync(Guid.NewGuid()));

    [Fact]
    public async Task ReplaceEntriesAsync_StartsTheRotationOver()
    {
        var service = Service();
        await PickAsync(service, 2);

        await service.ReplaceEntriesAsync(_playlist.Id, _playlist.Entries);

        Assert.Equal("A", (await service.SelectNextAsync(_playlist.Id))!.PresetName);
    }

    [Fact]
    public async Task ReplaceEntriesAsync_HoldsEverySettingToItsRange()
    {
        IReadOnlyList<VisualisationEntry>? saved = null;
        await _repository.ReplaceEntriesAsync(_playlist.Id, Arg.Do<IReadOnlyList<VisualisationEntry>>(entries => saved = entries));

        await Service().ReplaceEntriesAsync(_playlist.Id,
        [
            new() { PresetName = "Low", Brightness = -5, Saturation = -1, Sensitivity = -10 },
            new() { PresetName = "High", Brightness = 999, Saturation = 999, Sensitivity = 999 },
            new() { PresetName = "Inside", Brightness = 90, Saturation = 110, Sensitivity = 150 },
        ]);

        Assert.Equal(
            [
                (VisualisationEntry.MinBrightness, VisualisationEntry.MinSaturation, VisualisationEntry.MinSensitivity),
                (VisualisationEntry.MaxBrightness, VisualisationEntry.MaxSaturation, VisualisationEntry.MaxSensitivity),
                (90, 110, 150),
            ],
            saved!.Select(e => (e.Brightness, e.Saturation, e.Sensitivity)));
        Assert.Equal(["Low", "High", "Inside"], saved!.Select(e => e.PresetName));
    }

    [Fact]
    public async Task ReplaceEntriesAsync_HoldsTheBuiltInOptionsToWhatTheScreenDraws()
    {
        IReadOnlyList<VisualisationEntry>? saved = null;
        await _repository.ReplaceEntriesAsync(_playlist.Id, Arg.Do<IReadOnlyList<VisualisationEntry>>(entries => saved = entries));

        await Service().ReplaceEntriesAsync(_playlist.Id,
        [
            new() { PresetName = "a", BarCount = 20, ColourScheme = (VisualiserColourScheme)9, Colour = "red" },
            new() { PresetName = "b", BarCount = 50, ColourScheme = VisualiserColourScheme.Single, Colour = "#AABBCC" },
            new() { PresetName = "c", BarCount = 32, ColourScheme = VisualiserColourScheme.Theme, Colour = "#12345" },
        ]);

        Assert.Equal(
            [
                (16, VisualiserColourScheme.Classic, VisualisationEntry.DefaultColour),
                (64, VisualiserColourScheme.Single, "#aabbcc"),
                (32, VisualiserColourScheme.Theme, VisualisationEntry.DefaultColour),
            ],
            saved!.Select(e => (e.BarCount, e.ColourScheme, e.Colour)));
    }

    [Fact]
    public async Task ReplaceEntriesAsync_Announces()
    {
        var raised = 0;
        using var subscription = _broker.Subscribe<VisualisationPlaylistsChanged>(_ => raised++);

        Assert.True(await Service().ReplaceEntriesAsync(_playlist.Id, _playlist.Entries));
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task ReplaceEntriesAsync_NoSuchPlaylist_SavesNothingAndSaysSo()
    {
        var raised = 0;
        using var subscription = _broker.Subscribe<VisualisationPlaylistsChanged>(_ => raised++);

        Assert.False(await Service().ReplaceEntriesAsync(Guid.NewGuid(), [Entry("A")]));

        await _repository.DidNotReceiveWithAnyArgs().ReplaceEntriesAsync(default, default!);
        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task DeleteAsync_Announces()
    {
        _repository.DeleteAsync(_playlist.Id).Returns(true);
        var raised = 0;
        using var subscription = _broker.Subscribe<VisualisationPlaylistsChanged>(_ => raised++);

        Assert.True(await Service().DeleteAsync(_playlist.Id));
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task DeleteAsync_TheDefaultPlaylist_IsRefusedAndAnnouncesNothing()
    {
        var raised = 0;
        using var subscription = _broker.Subscribe<VisualisationPlaylistsChanged>(_ => raised++);

        Assert.False(await Service().DeleteAsync(VisualisationPlaylist.DefaultId));

        await _repository.DidNotReceiveWithAnyArgs().DeleteAsync(default);
        Assert.Equal(0, raised);
    }

    /// <summary>The guard is the built-in id prefix (RepositoryModels.IsBuiltIn), not an equality
    /// check against DefaultId specifically — any seeded row is refused the same way.</summary>
    [Fact]
    public async Task DeleteAsync_AnyBuiltInId_IsRefused()
    {
        var anotherBuiltIn = new Guid("00000000-0000-0000-0000-000000000099");

        Assert.False(await Service().DeleteAsync(anotherBuiltIn));

        await _repository.DidNotReceiveWithAnyArgs().DeleteAsync(default);
    }

    /// <summary>Hands out queued values, so a shuffle's picks are known in advance.</summary>
    private sealed class QueuedRandom : Random
    {
        public Queue<int> Values { get; } = new();

        public override int Next(int maxValue) => Values.Count > 0 ? Math.Min(Values.Dequeue(), maxValue - 1) : 0;
    }
}
