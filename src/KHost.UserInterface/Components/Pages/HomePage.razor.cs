using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.UserInterface.Components.Panels;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace KHost.UserInterface.Components.Pages;

public partial class HomePage : IAsyncDisposable
{
    [Inject] private ISingerQueueService? QueueService { get; set; }
    [Inject] private IMediaService? MediaService { get; set; }
    [Inject] private IJSRuntime? JS { get; set; }
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private bool _hasMedia;
    private bool _focusMediaSearchPending;

    private IJSObjectReference? _module;
    private IJSObjectReference? _handle;
    private MediaSearchPanel? _mediaSearchPanel;

    protected override async Task OnInitializedAsync()
    {
        _subscriptions.Add(Broker.Subscribe<SingerQueueChanged>(_ => InvokeAsync(StateHasChanged)));

        if (MediaService is not null)
            _hasMedia = await MediaService.HasAnyAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                if (JS is not null)
                {
                    _module = await JS.InvokeAsync<IJSObjectReference>("import", "/js/panel-resize.js");

                    _handle = await _module.InvokeAsync<IJSObjectReference>("init");
                }
            }
            catch
            {
            }

            await InvokeAsync(StateHasChanged);
        }

        // The panel does not exist until the render that the add put SelectedUser onto — checked
        // every render, not just the first, since it may already have existed for an earlier singer.
        if (_focusMediaSearchPending && _mediaSearchPanel is not null)
        {
            _focusMediaSearchPending = false;
            await _mediaSearchPanel.FocusQueryAsync();
        }
    }

    private void OnSingerAdded() => _focusMediaSearchPending = true;

    public async ValueTask DisposeAsync()
    {
        _subscriptions.Dispose();

        try
        {
            if (_handle is not null)
            {
                await _handle.InvokeVoidAsync("dispose");
                await _handle.DisposeAsync();
            }
            if (_module is not null)
                await _module.DisposeAsync();
        }
        catch
        {
        }
    }
}
