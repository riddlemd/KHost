using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Plugins.Sdk.Messaging;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>
/// Re-queueing from history is the one path that brings a key back. A song found through search is
/// a fresh start and carries none, so the recall has to live here rather than in the enqueue itself.
/// </summary>
public class SingerPerformanceHistoryDialogPitchTests : BunitContext
{
    private const string EnqueueSelector = ".kh-split-btn__primary";

    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly Guid _singerId = Guid.NewGuid();
    private readonly Media _media;

    public SingerPerformanceHistoryDialogPitchTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _media = new Media
        {
            Id = Guid.NewGuid(),
            FilePath = "/music/song.mp4",
            Title = "Africa",
            Artist = "Toto",
            Status = MediaStatus.Ready,
        };

        _mediaService.ReadAsync(_media.Id).Returns(_media);

        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings());

        Services.AddSingleton(_performances);
        Services.AddSingleton(_mediaService);
        Services.AddSingleton(appSettings);
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<IVenuesService>());
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    [Fact]
    public void Enqueue_CarriesTheKeyTheSongWasSungIn()
    {
        History(new Performance { Id = Guid.NewGuid(), SingerId = _singerId, MediaId = _media.Id, Pitch = -2 });

        var dialog = Render<SingerPerformanceHistoryDialog>(p => p
            .Add(d => d.IsOpen, true)
            .Add(d => d.UserId, _singerId));

        dialog.Find(EnqueueSelector).Click();

        _performances.Received(1).CreateAndEnqueueAsync(
            Arg.Is<Performance>(p => p.MediaId == _media.Id && p.Pitch == -2));
    }

    [Fact]
    public void Enqueue_TakesTheKeyFromTheRowClicked_NotTheNewestTake()
    {
        // Two takes of one song in different keys. The host picked a row; that is the key they
        // asked for, and quietly substituting the most recent one would ignore the choice.
        var older = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

        History(
            new Performance { Id = Guid.NewGuid(), SingerId = _singerId, MediaId = _media.Id, Pitch = 5, CreatedDate = DateTime.UtcNow },
            new Performance { Id = Guid.NewGuid(), SingerId = _singerId, MediaId = _media.Id, Pitch = -4, CreatedDate = older });

        var dialog = Render<SingerPerformanceHistoryDialog>(p => p
            .Add(d => d.IsOpen, true)
            .Add(d => d.UserId, _singerId));

        // Rows are newest first, so the second is the older take.
        dialog.FindAll(EnqueueSelector)[1].Click();

        _performances.Received(1).CreateAndEnqueueAsync(
            Arg.Is<Performance>(p => p.Pitch == -4));
    }

    [Fact]
    public void Row_ShowsTheKeyItWasSungIn()
    {
        History(new Performance { Id = Guid.NewGuid(), SingerId = _singerId, MediaId = _media.Id, Pitch = -2 });

        var dialog = Render<SingerPerformanceHistoryDialog>(p => p
            .Add(d => d.IsOpen, true)
            .Add(d => d.UserId, _singerId));

        // The host is choosing between takes; a key they cannot see is one they cannot choose by.
        Assert.Equal("−2", dialog.Find(".kh-singer-performance-history-dialog__pitch").TextContent.Trim());
    }

    [Fact]
    public void Row_ShowsNoKey_ForASongSungAsWritten()
    {
        History(new Performance { Id = Guid.NewGuid(), SingerId = _singerId, MediaId = _media.Id, Pitch = 0 });

        var dialog = Render<SingerPerformanceHistoryDialog>(p => p
            .Add(d => d.IsOpen, true)
            .Add(d => d.UserId, _singerId));

        // A "0" on every row of a long history is noise that hides the rows that were transposed.
        Assert.Empty(dialog.FindAll(".kh-singer-performance-history-dialog__pitch"));
    }

    private void History(params Performance[] performances) =>
        _performances
            .ReadBySingerIdAsync(_singerId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<PerformanceFilter>(), Arg.Any<DateTime?>())
            .Returns(new PaginatedResult<Performance>
            {
                Items = [.. performances],
                TotalCount = performances.Length,
                PageNumber = 1,
                PageSize = 25,
            });
}
