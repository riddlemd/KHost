using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.MediaProviders;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Components.Panels;

public partial class MediaSearchPanel : IDisposable
{
    [Inject] private IMediaSearchService? MediaSearchService { get; set; }
    [Inject] private ISingerQueueService? SingerQueueService { get; set; }
    [Inject] private IPerformanceService? PerformanceService { get; set; }
    [Inject] private IDialogService? DialogService { get; set; }
    [Inject] private IPermissionService? Permissions { get; set; }
    [Inject] private IMessageBroker Broker { get; set; } = default!;
    [Inject] private IControlState ControlState { get; set; } = default!;

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

    /// <summary>Puts the caret in the query field, so typing after adding a singer needs no mouse.</summary>
    public ValueTask FocusQueryAsync() => _queryInputRef.FocusAsync();

    protected override async Task OnInitializedAsync()
    {
        _subscriptions.Add(Broker.Subscribe<SingerQueueChanged>(_ => OnStateChanged()));
        // Someone else queueing a song changes this panel's badges without touching the singer
        // list, so the performance service has to be listened to as well.
        _subscriptions.Add(Broker.Subscribe<PerformancesChanged>(_ => OnStateChanged()));

        if (Permissions is not null)
            _canAddToQueue = await Permissions.HasAsync(KHostPermission.AddToQueue);

        // Without this the badges stay empty until some unrelated state change fires.
        await UpdateQueuedMediaAsync();
    }

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
        => (MediaSearchService?.Providers ?? []).Where(provider => provider != SearchTarget);

    private IMediaProvider? Provider(string? source)
        => MediaSearchService?.Providers.FirstOrDefault(provider =>
            string.Equals(provider.SourceName, source, StringComparison.OrdinalIgnoreCase));

    /// <summary>The picture for a row spanning the table, which declares no thumbnail column.</summary>
    private static string? OfferImage(MediaSearchEntity entity)
        => entity.Fields.GetValueOrDefault(MediaResultColumn.ThumbnailKey);

    private Task RunSearchAsync()
        => SearchTarget is { } provider
            ? RunSearchCoreAsync(provider.DisplayName, service => service.SearchAsync(_query, provider.SourceName))
            : RunSearchCoreAsync("the library", service => service.SearchAsync(_query));

    /// <summary>Picking a source only aims the button, since a remote provider is a metered call.</summary>
    private void SelectSource(string source) => ControlState.MediaSearchSource = source;

    /// <summary>Abandons the wait, not the work. The provider has no token to cancel by.</summary>
    private void CancelSearch() => _searchCts?.Cancel();

    private async Task RunSearchCoreAsync(
        string sourceLabel,
        Func<IMediaSearchService, Task<List<MediaSearchEntity>>> search)
    {
        if (MediaSearchService is null)
            return;

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
        if (e.Key == "Enter" && !_searching)
            await RunSearchAsync();
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
        if (PerformanceService is null || SingerQueueService is null) return;

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
