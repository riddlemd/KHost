using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

/// <summary>Add Venue passes null, not a stand-in Venue, so EditVenueDialog reads it as an add and
/// EditVenueModel.From(null, …) starts the new venue on the built-in visualisation playlist.</summary>
public class VenuesManagerPageAddTests : BunitContext
{
    private readonly IVenuesService _venuesService = Substitute.For<IVenuesService>();
    private readonly IDialogService _dialogService = Substitute.For<IDialogService>();

    public VenuesManagerPageAddTests()
    {
        this.AddKHostTestDefaults(dialogs: _dialogService);

        _venuesService.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<SortDescriptor?>())
            .Returns(Task.FromResult(new PaginatedResult<Venue> { Items = [], TotalCount = 0, PageNumber = 1, PageSize = AppSettings.DefaultPageSize }));

        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings());

        Services.AddSingleton(_venuesService);
        Services.AddSingleton(appSettings);
    }

    [Fact]
    public void AddVenueButton_OpensTheDialogWithNoVenue()
    {
        var cut = Render<VenuesManagerPage>();

        cut.Find(".kh-button--primary").Click();

        _dialogService.Received(1).RequestEditAsync(null, Arg.Any<Func<Venue?, Task>>());
    }
}
