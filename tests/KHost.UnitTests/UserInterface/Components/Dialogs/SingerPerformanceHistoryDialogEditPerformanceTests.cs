using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>A past performance is edited with the same dialog as a queued one, and the save rewrites
/// the record of how it was sung.</summary>
public class SingerPerformanceHistoryDialogEditPerformanceTests : BunitContext
{
    private const string EditAction = ".kh-singer-performance-history-dialog__edit-performance-btn";
    private const string Alias = "#edit-performance-sung-as";
    private const string Sliders = ".kh-song-control--slider .kh-song-control__track";
    private const string Save = ".kh-edit-performance-dialog__save-btn";

    private readonly DialogService _dialogs = new(NullLogger<DialogService>.Instance, []);
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly Guid _singerId = Guid.NewGuid();
    private readonly Media _media;
    private readonly Performance _sung;

    public SingerPerformanceHistoryDialogEditPerformanceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _media = new Media { Id = Guid.NewGuid(), FilePath = "/music/song.mp4", Title = "Africa", Status = MediaStatus.Ready };
        _sung = new Performance { Id = Guid.NewGuid(), SingerId = _singerId, MediaId = _media.Id, Pitch = -1, SungAs = "flo" };

        _mediaService.ReadAsync(_media.Id).Returns(_media);
        _performances
            .ReadBySingerIdAsync(_singerId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<PerformanceFilter>(), Arg.Any<DateTime?>())
            .Returns(_ => new PaginatedResult<Performance> { Items = [_sung], TotalCount = 1, PageNumber = 1, PageSize = 25 });
        _performances.ReadAsync(_sung.Id).Returns(_ => new Performance { Id = _sung.Id, MediaId = _media.Id, SungAs = "flo" });

        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings());
        var venues = Substitute.For<IVenuesService>();
        venues.ReadSelectedVenueAsync().Returns(new Venue { Name = "Bar", Settings = new() { AllowAliases = true } });
        var tracks = Substitute.For<IAudioTrackService>();
        tracks.ReadTracksAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AudioTrack>>([]));

        Services.AddSingleton<IDialogService>(_dialogs);
        Services.AddSingleton(_performances);
        Services.AddSingleton(_mediaService);
        Services.AddSingleton(appSettings);
        Services.AddSingleton(venues);
        Services.AddSingleton(tracks);
        Services.AddSingleton(Substitute.For<IPlaybackService>());
        var presets = Substitute.For<IVisualiserPresetService>();
        presets.ReadAll().Returns([]);
        Services.AddSingleton(presets);
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    [Fact]
    public async Task EditPerformance_OpensOnThePastTurn()
    {
        var host = await OpenHistoryAsync();

        host.Find(EditAction).Click();

        host.WaitForElement(Save);
        Assert.Equal("flo", host.Find(Alias).GetAttribute("value"));
        Assert.Equal("−1", host.Find(".kh-song-control__value").TextContent.Trim());
    }

    [Fact]
    public async Task Saving_RewritesTheRecordAndRereadsTheHistory()
    {
        var host = await OpenHistoryAsync();
        host.Find(EditAction).Click();
        host.WaitForElement(Save);

        host.Find(Alias).Change("DJ P");
        host.FindAll(Sliders)[0].Change("2");
        host.Find(Save).Click();

        host.WaitForAssertion(() => _performances.Received(1).UpdateSettingsAsync(
            _sung.Id, Arg.Is<PerformanceSettings>(s => s.Pitch == 2)));
        await _performances.Received(1).UpdateAsync(Arg.Is<Performance>(p => p.Id == _sung.Id && p.SungAs == "DJ P"));
        await _performances.Received(2).ReadBySingerIdAsync(
            _singerId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<PerformanceFilter>(), Arg.Any<DateTime?>());
    }

    private async Task<IRenderedComponent<DialogHost>> OpenHistoryAsync()
    {
        var host = Render<DialogHost>();
        await _dialogs.ShowSingerPerformanceHistoryAsync(_singerId);
        host.WaitForElement(EditAction);
        return host;
    }
}
