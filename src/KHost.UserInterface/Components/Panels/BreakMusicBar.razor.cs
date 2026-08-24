using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Panels;

public partial class BreakMusicBar : IDisposable
{
    [Inject] private IBreakMusicService BreakMusic { get; set; } = default!;
    [Inject] private IAdService Ads { get; set; } = default!;
    [Inject] private IFlashService Flash { get; set; } = default!;

    protected override void OnInitialized()
    {
        BreakMusic.StateChanged += OnStateChanged;
        Ads.StateChanged += OnStateChanged;
    }

    public void Dispose()
    {
        BreakMusic.StateChanged -= OnStateChanged;
        Ads.StateChanged -= OnStateChanged;

        GC.SuppressFinalize(this);
    }

    private async void OnStateChanged(object? sender, EventArgs e)
        => await InvokeAsync(StateHasChanged);

    private string DescribeState() => BreakMusic.State switch
    {
        BreakMusicState.Paused => "Break music paused",
        BreakMusicState.Suspended => "Break music waiting",
        _ => "Break music off",
    };

    private async Task PlayAsync()
    {
        // Resume rather than restart when it was paused, or the host loses their place in the
        // playlist every time they take the room down for an announcement.
        if (BreakMusic.State == BreakMusicState.Paused)
        {
            await BreakMusic.ResumeAsync();
            return;
        }

        // Told rather than left silent: with no playlist chosen this button looks broken, and the
        // fix is two pages away.
        if (!await BreakMusic.StartAsync())
            Flash.Show("Nothing to play — set a break music playlist on this venue.", FlashKind.Warning);
    }

    private Task PauseAsync() => BreakMusic.PauseAsync();

    private Task SkipAsync() => BreakMusic.SkipAsync();

    private async Task OnVolumeChangedAsync(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var percent))
            await BreakMusic.SetVolumeAsync(percent / 100f);
    }

    private async Task PlayAdAsync()
    {
        if (!await Ads.PlayNowAsync())
            Flash.Show("No ad played — the playlist is empty or a song is loaded.", FlashKind.Warning);
    }
}
