using KHost.Abstractions.Models;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Components.Pages.Settings;

/// <summary>The search+sort+page state every manager page's table shares, and the moves a click on
/// a column header, a page button or the search box makes against it. A plain class, not a base
/// class: the page still owns its own render, its own dialogs and its own subscriptions.</summary>
/// <remarks>Constructed after the page's own [Inject] properties are set — its <paramref
/// name="search"/> delegate closes over the service the page was injected — so build it in
/// OnInitializedAsync, not a field initializer.</remarks>
public sealed class PagedSearch<T>(Func<string, int, int, SortDescriptor?, Task<PaginatedResult<T>>> search)
{
    public int Page { get; private set; } = 1;
    public int Size { get; set; } = AppSettings.DefaultPageSize;
    public string Query { get; set; } = "";
    public string? SortColumn { get; private set; }
    public bool Descending { get; private set; }
    public PaginatedResult<T>? Result { get; private set; }

    /// <summary>Run after every read that actually reaches the service — never on a Previous/Next
    /// that stopped at an end — for a page that has to fetch something alongside each row, such as
    /// a tip total per user.</summary>
    public Func<PaginatedResult<T>, Task>? OnSearched { get; set; }

    public Task SearchAsync() => RunAsync();

    public Task SortByAsync(string column)
    {
        if (SortColumn == column)
            Descending = !Descending;
        else
        {
            SortColumn = column;
            Descending = false;
        }

        Page = 1;
        return RunAsync();
    }

    public Task SearchChangedAsync()
    {
        Page = 1;
        return RunAsync();
    }

    public Task PreviousAsync()
    {
        if (Page <= 1) return Task.CompletedTask;

        Page--;
        return RunAsync();
    }

    public Task NextAsync()
    {
        if (Page >= (Result?.TotalPages ?? 0)) return Task.CompletedTask;

        Page++;
        return RunAsync();
    }

    /// <summary>Reruns the search, and steps back a page when the one just read came back empty —
    /// deleting the last row on the last page, say — so the table is never left showing nothing
    /// while an earlier page still has rows.</summary>
    public async Task ReloadClampedAsync()
    {
        await RunAsync();

        if (Result?.Items.Count == 0 && Page > 1)
        {
            Page = Math.Max(1, Result.TotalPages);
            await RunAsync();
        }
    }

    private async Task RunAsync()
    {
        var sort = SortColumn is not null ? new SortDescriptor(SortColumn, Descending) : null;
        Result = await search(Query, Page, Size, sort);

        if (OnSearched is not null)
            await OnSearched(Result);
    }
}
