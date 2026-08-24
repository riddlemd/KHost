using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.MediaProviders;
using KHost.Plugins.Sdk.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;
using KHost.Plugins.Sdk.Models;
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

    private CancellationTokenSource? _searchCts;

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

    private Task RunSearchAsync()
        => RunSearchCoreAsync("the library", service => service.SearchAsync(_query));

    private Task RunProviderSearchAsync(string source, string displayName)
        => RunSearchCoreAsync(displayName, service => service.SearchAsync(_query, source));

    /// <summary>
    /// Abandons the wait rather than the work: a provider is handed no token, so its request runs
    /// on to completion in the background. The host gets the panel back either way.
    /// </summary>
    private void CancelSearch() => _searchCts?.Cancel();

    private async Task RunSearchCoreAsync(
        string sourceLabel,
        Func<IMediaSearchService, Task<List<MediaSearchEntity>>> search)
    {
        if (MediaSearchService is null)
            return;

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

    private async Task PerformActionAsync(Func<MediaSearchEntity, Task> func, MediaSearchEntity mediaSearchEntity)
    {
        try
        {
            await func(mediaSearchEntity);
        }
        catch (OperationCanceledException)
        {
            // The host dequeuing the Downloading row cancels the plugin's own download token —
            // this is that cancel unwinding through the action, not a failure to report.
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

    /// <summary>
    /// Every singer's queue, not just the selected one — a song already claimed by anyone in the
    /// room is worth knowing about before handing it to someone else.
    /// </summary>
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
