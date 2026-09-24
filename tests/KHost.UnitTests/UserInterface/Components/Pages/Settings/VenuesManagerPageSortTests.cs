using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Abstractions.Messaging;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

/// <summary>
/// A sort click used to fire SearchAsync without awaiting it (`_ = SearchAsync()`), so the click
/// handler returned before the query came back and nothing re-rendered once it did.
/// </summary>
public class VenuesManagerPageSortTests : BunitContext
{
    private readonly IVenuesService _venuesService = Substitute.For<IVenuesService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly TaskCompletionSource<PaginatedResult<Venue>> _sortedResult = new();

    public VenuesManagerPageSortTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _venuesService
            .SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Is<SortDescriptor?>(s => s == null))
            .Returns(Task.FromResult(ResultOf("Alpha")));

        // The sort click's own query resolves only once the test lets it, so a render that happens
        // before completion (the old fire-and-forget) is distinguishable from one that waits.
        _venuesService
            .SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Is<SortDescriptor?>(s => s != null && s.Column == "name"))
            .Returns(_sortedResult.Task);

        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings());

        Services.AddSingleton(_venuesService);
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(appSettings);
        Services.AddSingleton<IMessageBroker>(_broker);

        var auth = AddAuthorization();
        auth.SetAuthorized("tester");
        auth.SetPolicies("EditVenue", "DeleteVenue");
    }

    [Fact]
    public async Task SortColumnClicked_OnceTheQueryReturns_ShowsTheSortedRowsWithNoFurtherEvent()
    {
        var cut = Render<CascadingAuthenticationState>(ps => ps.AddChildContent<VenuesManagerPage>());
        Assert.Contains("Alpha", cut.Markup);

        // The Name column is the first sortable header in VenuesManagerPage.razor.
        cut.FindAll("th.kh-table__col--sortable")[0].Click();

        // The query for the click is still pending, so the stale row must still be showing.
        Assert.Contains("Alpha", cut.Markup);
        Assert.DoesNotContain("Beta", cut.Markup);

        _sortedResult.SetResult(ResultOf("Beta"));

        // Nothing else pokes the component after this: the fix is that the click handler's own
        // await drives the re-render once SearchAsync completes.
        cut.WaitForAssertion(() => Assert.Contains("Beta", cut.Markup));
        Assert.DoesNotContain("Alpha", cut.Markup);
    }

    private static PaginatedResult<Venue> ResultOf(string name) => new()
    {
        Items = [new Venue { Name = name }],
        TotalCount = 1,
        PageNumber = 1,
        PageSize = AppSettings.DefaultPageSize,
    };
}
