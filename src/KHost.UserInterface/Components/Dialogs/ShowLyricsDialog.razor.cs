using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

public partial class ShowLyricsDialog
{
    private const string _rootClassName = "kh-show-lyrics-dialog";

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public string Query { get; set; } = "";

    [Parameter] public EventCallback OnClose { get; set; }

    [Inject] private ILyricsService LyricsService { get; set; } = default!;

    private Lyrics? _lyrics;
    private bool _loading;

    // DialogHost keys every dialog by request id, so a fresh instance is created per open; this
    // runs exactly once with Query already bound.
    protected override async Task OnInitializedAsync()
    {
        _loading = true;
        StateHasChanged();

        _lyrics = await LyricsService.SearchAsync(Query);
        _loading = false;
    }

    public async Task CloseAsync()
    {
        IsOpen = false;
        await OnClose.InvokeAsync();
    }

    public record DialogRequest(string Query, Action? OnClose) : BaseDialogRequest(OnClose);
}
