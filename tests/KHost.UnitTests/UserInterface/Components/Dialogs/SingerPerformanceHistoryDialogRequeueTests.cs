using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Abstractions.Messaging;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>Which songs a host can put back in the queue from a singer's history.</summary>
public class SingerPerformanceHistoryDialogRequeueTests : BunitContext
{
    private const string EnqueueSelector = ".kh-split-btn__primary";

    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly Guid _singerId = Guid.NewGuid();

    public SingerPerformanceHistoryDialogRequeueTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings());

        Services.AddSingleton(_performances);
        Services.AddSingleton(_mediaService);
        Services.AddSingleton(appSettings);
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<IVenuesService>());
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    private IRenderedComponent<SingerPerformanceHistoryDialog> RenderWithSongIn(MediaStatus status)
    {
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/karaoke/youtube/abc.mp4", Title = "Africa", Artist = "Toto", Status = status };
        _mediaService.ReadAsync(media.Id).Returns(media);
        _performances
            .ReadBySingerIdAsync(_singerId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<PerformanceFilter>(), Arg.Any<DateTime?>())
            .Returns(new PaginatedResult<Performance>
            {
                Items = [new Performance { Id = Guid.NewGuid(), SingerId = _singerId, MediaId = media.Id }],
                TotalCount = 1,
                PageNumber = 1,
                PageSize = 25,
            });

        return Render<SingerPerformanceHistoryDialog>(p => p.Add(d => d.IsOpen, true).Add(d => d.UserId, _singerId));
    }

    private static List<string> Titles(IRenderedComponent<SingerPerformanceHistoryDialog> dialog)
        => [.. dialog.FindAll("[title]").Select(e => e.GetAttribute("title")!)];

    /// <summary>Queuing a song whose file was removed is what fetches it again, so history must offer it.</summary>
    [Fact]
    public void ASongWhoseFileWasRemoved_CanBeQueuedAgain_AndSaysItDownloads()
    {
        var dialog = RenderWithSongIn(MediaStatus.NotDownloaded);

        var enqueue = dialog.Find(EnqueueSelector);
        Assert.False(enqueue.HasAttribute("disabled"));
        Assert.Contains(Titles(dialog), t => t.Contains("downloads it again"));

        enqueue.Click();

        _performances.Received(1).CreateAndEnqueueAsync(Arg.Any<Performance>());
    }

    [Fact]
    public void AReadySong_CanBeQueuedAgain()
    {
        var dialog = RenderWithSongIn(MediaStatus.Ready);

        Assert.False(dialog.Find(EnqueueSelector).HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(MediaStatus.Broken, "Broken")]
    [InlineData(MediaStatus.Downloading, "Downloading")]
    public void ASongThatCannotQueue_IsDisabled_AndSaysWhy(MediaStatus status, string says)
    {
        var dialog = RenderWithSongIn(status);

        Assert.True(dialog.Find(EnqueueSelector).HasAttribute("disabled"));
        Assert.Contains(says, Titles(dialog));
    }
}
