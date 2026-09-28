using KHost.Abstractions.Models;
using KHost.UserInterface.Components.Pages.Settings;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

public class PagedSearchTests
{
    // PageSize 1 and TotalCount 2 gives two pages of one row each, cheaply.
    private static PaginatedResult<string> TwoPagesOf(int page, int itemCount = 1)
        => new() { Items = [.. Enumerable.Repeat("row", itemCount)], TotalCount = 2, PageNumber = page, PageSize = 1 };

    [Fact]
    public async Task SortByAsync_TheSameColumnTwice_TogglesDescending()
    {
        SortDescriptor? lastSort = null;
        var search = new PagedSearch<string>((q, p, s, sort) => { lastSort = sort; return Task.FromResult(TwoPagesOf(p)); });

        await search.SortByAsync("name");
        Assert.False(search.Descending);
        Assert.Equal("name", lastSort!.Column);

        await search.SortByAsync("name");
        Assert.True(search.Descending);
    }

    [Fact]
    public async Task SortByAsync_ADifferentColumn_ResetsDescendingAndSortsAscending()
    {
        var search = new PagedSearch<string>((q, p, s, sort) => Task.FromResult(TwoPagesOf(p)));

        await search.SortByAsync("name");
        await search.SortByAsync("name"); // descending now
        await search.SortByAsync("enabled");

        Assert.Equal("enabled", search.SortColumn);
        Assert.False(search.Descending);
    }

    [Fact]
    public async Task SortByAsync_ResetsToTheFirstPage()
    {
        var search = new PagedSearch<string>((q, p, s, sort) => Task.FromResult(TwoPagesOf(p)));
        await search.SearchAsync();
        await search.NextAsync();
        Assert.Equal(2, search.Page); // sanity: the move worked before the reset is asserted

        await search.SortByAsync("name");

        Assert.Equal(1, search.Page);
    }

    [Fact]
    public async Task SearchChangedAsync_ResetsToTheFirstPage()
    {
        var search = new PagedSearch<string>((q, p, s, sort) => Task.FromResult(TwoPagesOf(p)));
        await search.SearchAsync();
        await search.NextAsync();

        await search.SearchChangedAsync();

        Assert.Equal(1, search.Page);
    }

    [Fact]
    public async Task ReloadClampedAsync_StepsBack_WhenTheCurrentPageCameBackEmpty()
    {
        // Simulates deleting the only row on page 2 of 2: the total shrinks to one row/one page,
        // so the read that follows the clamp is what a host should end up seeing.
        var search = new PagedSearch<string>((q, p, s, sort) => Task.FromResult(p == 2
            // Page 2 now empty, and the total has shrunk to the one row still on page 1.
            ? new PaginatedResult<string> { Items = [], TotalCount = 1, PageNumber = 2, PageSize = 1 }
            : TwoPagesOf(p)));

        await search.SearchAsync();
        await search.NextAsync();
        Assert.Equal(2, search.Page);

        await search.ReloadClampedAsync();

        Assert.Equal(1, search.Page);
        Assert.Single(search.Result!.Items);
    }

    [Fact]
    public async Task ReloadClampedAsync_DoesNothingExtra_WhenTheCurrentPageStillHasRows()
    {
        var calls = 0;
        var search = new PagedSearch<string>((q, p, s, sort) => { calls++; return Task.FromResult(TwoPagesOf(p)); });

        await search.ReloadClampedAsync();

        Assert.Equal(1, calls);
    }

    // Page 1 coming back empty (nothing matches at all) is not "clamp back a page" — there is no
    // earlier page — so a second read here would be a needless round trip, forever.
    [Fact]
    public async Task ReloadClampedAsync_DoesNothingExtra_WhenPageOneIsEmpty()
    {
        var calls = 0;
        var search = new PagedSearch<string>((q, p, s, sort) =>
        {
            calls++;
            return Task.FromResult(new PaginatedResult<string> { Items = [], TotalCount = 0, PageNumber = 1, PageSize = 1 });
        });

        await search.ReloadClampedAsync();

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task NextAsync_DoesNothing_OnTheLastPage()
    {
        var search = new PagedSearch<string>((q, p, s, sort) => Task.FromResult(TwoPagesOf(p)));
        await search.SearchAsync();
        await search.NextAsync();
        Assert.Equal(2, search.Page);

        await search.NextAsync();

        Assert.Equal(2, search.Page);
    }

    [Fact]
    public async Task PreviousAsync_DoesNothing_OnTheFirstPage()
    {
        var search = new PagedSearch<string>((q, p, s, sort) => Task.FromResult(TwoPagesOf(p)));

        await search.PreviousAsync();

        Assert.Equal(1, search.Page);
    }
}
