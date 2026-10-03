using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.MediaProviders;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Components.Panels;

public partial class MediaSearchPanel : IDisposable
{
    [Inject] private IMediaSearchService MediaSearchService { get; set; } = default!;
    [Inject] private ISingerQueueService SingerQueueService { get; set; } = default!;
    [Inject] private IPerformanceService PerformanceService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private IPermissionService Permissions { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;
    [Inject] private IControlState ControlState { get; set; } = default!;
    [Inject] private IAppSettingsService AppSettings { get; set; } = default!;
    [Inject] private ICacheService CacheService { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IFlashService Flash { get; set; } = default!;
    [Inject] private ILogger<MediaSearchPanel> Logger { get; set; } = default!;

    /// <summary>Where "Remember the last one used" keeps its pick: per machine, like the theme and
    /// the selected venue, not per venue.</summary>
    private const string LastSearchModeCacheKey = "search-mode-last-used";

    /// <summary>Rows drawn at most; a longer list is cut here, and the keyboard walks only these.</summary>
    private const int MaxVisibleResults = 300;

    private readonly SubscriptionSet _subscriptions = new();

    private bool _searching;
    private string _query = "";
    private List<MediaSearchEntity>? _results;
    private string? _errorMessage;
    private IEnumerable<MediaProviderAction> _globalActions = [];
    private bool _canAddToQueue;
    private Dictionary<Guid, List<string>> _queuedBySinger = [];

    /// <summary>What is being searched, for the wait message. Null while nothing is running.</summary>
    private string? _searchingSource;

    /// <summary>The last search run, so an action that invalidates results can repeat it.</summary>
    private Func<IMediaSearchService, Task<List<MediaSearchEntity>>>? _lastSearch;
    private string _lastSearchSource = "the library";

    private CancellationTokenSource? _searchCts;

    private ElementReference _queryInputRef;
    private ElementReference _resultsRef;

    /// <summary>The row the keyboard is on, or -1. Reset by every search: an index into the old
    /// results would land on a different song.</summary>
    private int _selectedResultIndex = -1;
    private int _lastScrolledResultIndex = -1;

    /// <summary>Puts the caret in the query field, so typing after adding a singer needs no mouse.</summary>
    public ValueTask FocusQueryAsync() => _queryInputRef.FocusAsync();

    protected override async Task OnInitializedAsync()
    {
        _subscriptions.Add(Broker.Subscribe<SingerQueueChanged>(_ => OnStateChanged()));
        // Someone else queueing a song changes this panel's badges without touching the singer
        // list, so the performance service has to be listened to as well.
        _subscriptions.Add(Broker.Subscribe<PerformancesChanged>(_ => OnStateChanged()));

        _canAddToQueue = await Permissions.HasAsync(KHostPermission.AddToQueue);

        await SeedSearchModeAsync();

        // Without this the badges stay empty until some unrelated state change fires.
        await UpdateQueuedMediaAsync();
    }

    /// <summary>Starts the panel in App Settings' configured mode, or — with "Remember" — in
    /// whatever mode was last picked on this machine.</summary>
    /// <remarks>Only the first time this circuit builds the panel: a later rebuild (the selected
    /// singer changing, say) must not override a pick already made this session.</remarks>
    private async Task SeedSearchModeAsync()
    {
        if (ControlState.MediaSearchSource is not null)
            return;

        var configured = AppSettings.Current.DefaultSearchMode;

        ControlState.MediaSearchSource = configured == KHost.UserInterface.Services.AppSettings.RememberLastSearchMode
            ? await CacheService.LoadAsync<string>(LastSearchModeCacheKey)
            : configured;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Only on a change: scrolling every render would yank a list the host is scrolling by hand.
        if (_selectedResultIndex == _lastScrolledResultIndex) return;

        _lastScrolledResultIndex = _selectedResultIndex;
        if (_selectedResultIndex < 0) return;

        try
        {
            await JS.InvokeVoidAsync("scrollIntoViewSmooth", ".kh-media-search-panel__results__result--selected");
        }
        catch (JSDisconnectedException) { }
    }

    private IReadOnlyList<MediaSearchEntity> VisibleResults
        => _results is null || _searching ? [] : _results.Take(MaxVisibleResults).ToList();

    /// <summary>Column classes: width columns keep their width, text columns split what's left.</summary>
    private static string ColumnClass(IReadOnlyList<MediaResultColumn> columns, int index)
    {
        var classes = columns[index].EffectiveKind switch
        {
            MediaResultColumnKind.Thumbnail => "kh-media-search-panel__col--thumb",
            MediaResultColumnKind.Duration => "kh-media-search-panel__col--duration",
            MediaResultColumnKind.Label => "kh-media-search-panel__col--label",
            _ => "kh-media-search-panel__col--text",
        };

        var shed = MediaResultColumnSet.ShedOrder(columns, index);

        if (shed > 0)
            classes += $" kh-media-search-panel__col--shed{Math.Min(shed, 2)}";

        return classes;
    }

    /// <summary>What a plain search press reaches: the last source picked, or the library.</summary>
    private IMediaProvider? SearchTarget
        => Provider(ControlState.MediaSearchSource) ?? Provider(nameof(LocalMediaProvider));

    /// <summary>Names the button, so the host reads where a press goes without opening the list.</summary>
    private string SearchTargetLabel => SearchTarget?.DisplayName ?? "Library";

    /// <summary>Omits the source already on the button: picking it again would be redundant.</summary>
    private IEnumerable<IMediaProvider> UnselectedProviders
        => MediaSearchService.Providers.Where(provider => provider != SearchTarget);

    private IMediaProvider? Provider(string? source)
        => MediaSearchService.Providers.FirstOrDefault(provider =>
            string.Equals(provider.SourceName, source, StringComparison.OrdinalIgnoreCase));

    /// <summary>The picture for a row spanning the table, which declares no thumbnail column.</summary>
    private static string? OfferImage(MediaSearchEntity entity)
        => entity.Fields.GetValueOrDefault(MediaResultColumn.ThumbnailKey);

    private Task RunSearchAsync()
        => SearchTarget is { } provider
            ? RunSearchCoreAsync(provider.DisplayName, service => service.SearchAsync(_query, provider.SourceName))
            : RunSearchCoreAsync("the library", service => service.SearchAsync(_query));

    /// <summary>Picking a source only aims the button, since a remote provider is a metered call.
    /// With "Remember the last one used" configured, the pick also becomes next time's start;
    /// with a fixed default, the pick is just for now.</summary>
    private async Task SelectSourceAsync(string source)
    {
        ControlState.MediaSearchSource = source;

        if (AppSettings.Current.DefaultSearchMode == KHost.UserInterface.Services.AppSettings.RememberLastSearchMode)
            await CacheService.SaveAsync(LastSearchModeCacheKey, source);
    }

    /// <summary>Abandons the wait, not the work. The provider has no token to cancel by.</summary>
    private void CancelSearch() => _searchCts?.Cancel();

    private async Task RunSearchCoreAsync(
        string sourceLabel,
        Func<IMediaSearchService, Task<List<MediaSearchEntity>>> search)
    {
        _lastSearch = search;
        _lastSearchSource = sourceLabel;

        // A second search supersedes the first, so the one still running stops owning the panel.
        _searchCts?.Cancel();
        _searchCts?.Dispose();

        var cts = new CancellationTokenSource();
        _searchCts = cts;

        _errorMessage = null;
        _searching = true;
        _searchingSource = sourceLabel;
        _results = null;
        _selectedResultIndex = -1;

        StateHasChanged();

        try
        {
            _results = await search(MediaSearchService).WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            // Safe unguarded: cancelling makes the await above throw at once, so this method has
            // already returned before the button is live enough to start another search.
            _searching = false;
            _searchingSource = null;

            StateHasChanged();
        }
    }

    private async Task OnFilterKeyDownAsync(KeyboardEventArgs e)
    {
        var modified = e.CtrlKey || e.MetaKey || e.ShiftKey;

        if (e.Key == "Enter" && !_searching)
        {
            await RunSearchAsync();
            return;
        }

        // shortcuts.js cancels the default (back on Windows, a word jump on a Mac) for this box.
        if (e.AltKey && !modified && e.Key is "ArrowLeft" or "ArrowRight")
        {
            await CycleSearchModeAsync(forward: e.Key == "ArrowRight");
            return;
        }

        if (e.Key == "ArrowDown" && !modified && !e.AltKey && VisibleResults.Count > 0)
        {
            _selectedResultIndex = 0;
            await _resultsRef.FocusAsync();
        }
    }

    /// <summary>Steps the source the way picking one from the button's list does, wrapping, and
    /// likewise runs no search: a remote provider is a metered call.</summary>
    private async Task CycleSearchModeAsync(bool forward)
    {
        var providers = MediaSearchService.Providers.ToList();
        if (providers.Count < 2) return;

        var current = SearchTarget is { } target ? providers.IndexOf(target) : -1;
        var next = current < 0
            ? (forward ? 0 : providers.Count - 1)
            : (current + (forward ? 1 : -1) + providers.Count) % providers.Count;

        await SelectSourceAsync(providers[next].SourceName);
    }

    private async Task OnResultsKeyDownAsync(KeyboardEventArgs e)
    {
        var rows = VisibleResults;
        if (rows.Count == 0 || e.CtrlKey || e.MetaKey || e.AltKey) return;

        if (e.Key == "Enter")
        {
            if (_selectedResultIndex >= 0 && _selectedResultIndex < rows.Count)
                await EnqueueFromKeyboardAsync(rows[_selectedResultIndex]);
            return;
        }

        // Up off the first row is back to the box, so typing a new search needs no mouse.
        if (e.Key == "ArrowUp" && !e.ShiftKey && _selectedResultIndex <= 0)
        {
            _selectedResultIndex = -1;
            await _queryInputRef.FocusAsync();
            return;
        }

        // Shift would reorder, which results cannot; plain arrows walk the rows.
        var action = ListKeyboardShortcuts.Resolve(e.Key, e.ShiftKey, _selectedResultIndex, rows.Count);

        await ListKeyboardShortcuts.DispatchAsync(
            action, _selectedResultIndex, canReorder: false,
            select: idx =>
            {
                _selectedResultIndex = idx;
                return Task.CompletedTask;
            },
            move: _ => Task.CompletedTask);
    }

    /// <summary>The row's first action button, under the same conditions that leave it enabled.</summary>
    private Task EnqueueFromKeyboardAsync(MediaSearchEntity entity)
    {
        if (!_canAddToQueue || SingerQueueService.SelectedUserId is null)
            return Task.CompletedTask;

        return CombineWithGlobalActions(entity.SupportedActions).FirstOrDefault() is { } action
            ? PerformActionAsync(action, entity)
            : Task.CompletedTask;
    }

    private async Task PerformActionAsync(MediaProviderAction action, MediaSearchEntity mediaSearchEntity)
    {
        try
        {
            await action.PerformAsync(mediaSearchEntity);
        }
        catch (OperationCanceledException)
        {
            // The host dequeuing the Downloading row cancels the plugin's own download token.
            // This is that cancel unwinding through the action, not a failure to report.
            return;
        }
        catch (Exception ex)
        {
            // Escaping an @onclick ends the circuit, and the show with it.
            Logger.LogWarning(ex, "Action '{Action}' failed for '{Title}'", action.DisplayName, mediaSearchEntity.Title);
            Flash.Show(NetworkFailureText.Describe(mediaSearchEntity.SourceDisplayName, ex), FlashType.Warning);
            return;
        }

        // Signing in is the case this exists for: the row the host clicked was the provider saying
        // it had nothing to search with, so leaving it on screen reads as the sign-in not working.
        if (action.RefreshesResults && _lastSearch is { } search)
        {
            await RunSearchCoreAsync(_lastSearchSource, search);
            return;
        }

        await InvokeAsync(StateHasChanged);
    }

    private MediaProviderAction[] CombineWithGlobalActions(IEnumerable<MediaProviderAction> actions)
        => [.._globalActions, ..actions];

    private void OnStateChanged() => InvokeAsync(async () =>
    {
        await UpdateQueuedMediaAsync();
        StateHasChanged();
    });

    /// <summary>Tracks what each singer already queued, so a claimed song is not offered again.</summary>
    private async Task UpdateQueuedMediaAsync()
    {
        var singerNames = SingerQueueService.Users.ToDictionary(user => user.Id, user => user.Name);
        var queued = await PerformanceService.ReadQueuedAsync();

        _queuedBySinger = queued
            .Where(performance => singerNames.ContainsKey(performance.SingerId))
            .GroupBy(performance => performance.MediaId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(performance => singerNames[performance.SingerId]).Distinct().ToList());
    }

    /// <summary>Shows every name, not a count, since the badge's glyph alone can't say who.</summary>
    private static string QueuedByLabel(IReadOnlyList<string> queuedBy)
        => $"Already queued by {string.Join(", ", queuedBy)}";

    private List<string> GetSingersWithMediaQueued(string foreignKey, string source)
    {
        if (!source.Equals(nameof(LocalMediaProvider), StringComparison.InvariantCultureIgnoreCase))
            return [];

        return Guid.TryParse(foreignKey, out var mediaId) && _queuedBySinger.TryGetValue(mediaId, out var singers)
            ? singers
            : [];
    }

    public void Dispose()
    {
        _subscriptions.Dispose();

        // A search still running would otherwise call StateHasChanged on a disposed component.
        _searchCts?.Cancel();
        _searchCts?.Dispose();
    }
}
