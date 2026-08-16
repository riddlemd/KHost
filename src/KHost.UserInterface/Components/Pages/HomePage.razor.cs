using KHost.Abstractions.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace KHost.UserInterface.Components.Pages;

public partial class HomePage : IAsyncDisposable
{
    [Inject] private ISingerQueueService? QueueService { get; set; }
    [Inject] private IMediaService? MediaService { get; set; }
    [Inject] private IJSRuntime? JS { get; set; }

    private bool _hasMedia;

    private IJSObjectReference? _module;
    private IJSObjectReference? _handle;

    protected override async Task OnInitializedAsync()
    {
        if (QueueService is not null)
            QueueService.StateChanged += OnStateChanged;

        if (MediaService is not null)
            _hasMedia = await MediaService.HasAnyAsync();
    }

    private void OnStateChanged(object? sender, EventArgs e) => InvokeAsync(StateHasChanged);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

try
        {
            if (JS is null) return;

            _module = await JS.InvokeAsync<IJSObjectReference>("import", "/js/panel-resize.js");

            _handle = await _module.InvokeAsync<IJSObjectReference>("init");
        }
        catch
        {
            // Do nothing...
        }

        await InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        if (QueueService is not null)
            QueueService.StateChanged -= OnStateChanged;

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
            // Do nothing...
        }
    }
}
