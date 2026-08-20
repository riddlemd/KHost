using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.MediaProviders;
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

    private bool _searching;
    private string _query = "";
    private List<MediaSearchEntity>? _results;
    private string? _errorMessage;
    private IEnumerable<MediaProviderAction> _globalActions = [];
    private bool _multipleProvidersInResults;
    private bool _canAddToQueue;
    private Dictionary<Guid, List<string>> _queuedBySinger = [];

    protected override async Task OnInitializedAsync()
    {
        if (Permissions is not null)
            _canAddToQueue = await Permissions.HasAsync(KHostPermission.AddToQueue);

        SingerQueueService?.StateChanged += OnStateChanged;
        // Someone else queueing a song changes this panel's badges without touching the singer
        // list, so the performance service has to be listened to as well.
        PerformanceService?.StateChanged += OnStateChanged;

        // Without this the badges stay empty until some unrelated state change fires.
        await UpdateQueuedMediaAsync();
    }

    private Task RunSearchAsync() => RunSearchCoreAsync(service => service.SearchAsync(_query));

    private Task RunProviderSearchAsync(string source)
        => RunSearchCoreAsync(service => service.SearchAsync(_query, source));

    private Task RunAllProvidersSearchAsync() => RunSearchCoreAsync(service => service.SearchAllAsync(_query));

    private async Task RunSearchCoreAsync(Func<IMediaSearchService, Task<List<MediaSearchEntity>>> search)
    {
        if (MediaSearchService is null)
            return;

        _errorMessage = null;
        _searching = true;
        _results = null;

        StateHasChanged();

        _results = await search(MediaSearchService);

        _searching = false;

        _multipleProvidersInResults = _results.GroupBy(x => x.Source).Distinct().Count() > 1;
    }

    private async Task OnFilterKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !_searching)
            await RunSearchAsync();
    }

    private async Task PerformActionAsync(Func<MediaSearchEntity, Task> func, MediaSearchEntity mediaSearchEntity)
    {
        await func(mediaSearchEntity);

        await InvokeAsync(StateHasChanged);
    }

    private MediaProviderAction[] CombineWithGlobalActions(IEnumerable<MediaProviderAction> actions)
        => [.._globalActions, ..actions];

    private void OnStateChanged(object? sender, EventArgs e) => InvokeAsync(async () =>
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
        SingerQueueService?.StateChanged -= OnStateChanged;
        PerformanceService?.StateChanged -= OnStateChanged;
    }
}
