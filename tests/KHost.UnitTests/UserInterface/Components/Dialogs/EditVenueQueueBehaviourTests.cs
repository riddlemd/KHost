using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.QueueRotation;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using KHost.Domain.Services;
using KHost.Domain.Services.QrCodes;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>The rotation dialog moved onto the "Queue behavior" section when EditVenueDialog was
/// split into section components (W9b): it edits Model.QueueRotation in place, so it now belongs
/// with the button that opens it rather than back up at the top-level dialog.</summary>
public class EditVenueQueueBehaviourTests : BunitContext
{
    private const string RotationButtonSelector = ".kh-venue-queue-behaviour__rotation-btn";
    private const string ScrimSelector = ".kh-scrim";

    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly IMediaPoolService _mediaPools = Substitute.For<IMediaPoolService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    public EditVenueQueueBehaviourTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAppSettings();

        _mediaPools.ReadAllWithEntriesAsync(Arg.Any<PoolPurpose>(), Arg.Any<Guid?>())
            .Returns(new List<MediaPool>());
        _breakMusic.Providers.Returns(new List<IBreakMusicProvider>());

        Services.AddSingleton(_breakMusic);
        Services.AddSingleton(Substitute.For<IQrCodePngExporter>());
        Services.AddSingleton(Substitute.For<IMediaUploader>());
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton(_mediaPools);

        // The dialog reads the visualisation playlists as it opens; none is all these need.
        var visualisations = Substitute.For<IVisualisationPlaylistService>();
        visualisations.ReadAllWithEntriesAsync().Returns(new List<VisualisationPlaylist>());
        Services.AddSingleton(visualisations);
        Services.AddSingleton(_media);
        Services.AddSingleton<IMessageBroker>(_broker);


        var plugins = Substitute.For<IPluginRegistry>();
        plugins.Plugins.Returns([]);
        Services.AddSingleton(plugins);

        // Only reached once the rotation dialog itself opens; every other EditVenueDialog test
        // never clicks the button, so it never needs these.
        Services.AddSingleton(Substitute.For<IQueueRotationStrategyFactory>());

        var userGroups = Substitute.For<IUserGroupsService>();
        userGroups.ReadAllAsync(1, Arg.Any<int>())
            .Returns(new PaginatedResult<KHostUserGroup> { Items = [], TotalCount = 0, PageNumber = 1, PageSize = 1000 });
        Services.AddSingleton(userGroups);
    }

    // Ticked through the rendered box: a binding wired to the wrong field passes any model test.
    [Theory]
    [InlineData("#venue-clear-queue", nameof(EditVenueModel.ClearQueueOnClose))]
    [InlineData("#venue-clear-queued-songs", nameof(EditVenueModel.ClearQueuedSongsOnClose))]
    [InlineData("#venue-remove-unsung-singers", nameof(EditVenueModel.RemoveUnsungSingersOnClose))]
    public void ClosingBoxes_EachSetsItsOwnSetting(string box, string setting)
    {
        var model = new EditVenueModel
        {
            ClearQueueOnClose = false,
            ClearQueuedSongsOnClose = false,
            RemoveUnsungSingersOnClose = false,
        };
        // The dialog's form supplies this; its validation messages throw without one.
        var cut = Render<EditVenueQueueBehaviour>(parameters => parameters
            .Add(p => p.Model, model)
            .AddCascadingValue(new Microsoft.AspNetCore.Components.Forms.EditContext(model)));

        cut.Find(box).Change(true);

        bool Read(string name) => (bool)typeof(EditVenueModel).GetProperty(name)!.GetValue(model)!;

        Assert.True(Read(setting));
        Assert.All(
            new[] { nameof(EditVenueModel.ClearQueueOnClose), nameof(EditVenueModel.ClearQueuedSongsOnClose), nameof(EditVenueModel.RemoveUnsungSingersOnClose) }
                .Where(name => name != setting),
            name => Assert.False(Read(name)));
    }

    [Fact]
    public void RotationButton_Clicked_OpensTheRotationDialog()
    {
        var cut = Render<EditVenueDialog>(parameters => parameters.Add(p => p.IsOpen, true));

        // One scrim already, for the venue dialog itself.
        Assert.Single(cut.FindAll(ScrimSelector));

        cut.Find(RotationButtonSelector).Click();

        // A second, for the rotation dialog now open on top of it.
        Assert.Equal(2, cut.FindAll(ScrimSelector).Count);
    }
}
