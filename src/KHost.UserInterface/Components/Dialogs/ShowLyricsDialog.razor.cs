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
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public bool CloseOnScrimClick { get; set; }

    [Parameter] public EventCallback OnClose { get; set; }

    [Inject] private ILyricsService LyricsService { get; set; } = default!;

    private Lyrics? _lyrics;
    private bool _loading;
    private bool _prevIsOpen;

    protected override async Task OnParametersSetAsync()
    {
        if (IsOpen && !_prevIsOpen)
        {
            _loading = true;
            StateHasChanged();

            _lyrics = await LyricsService.SearchAsync(Query);
            _loading = false;
        }
        _prevIsOpen = IsOpen;
    }

    public async Task CloseAsync()
    {
        IsOpen = false;
        await OnClose.InvokeAsync();
    }

    public record DialogRequest(string Query, Action? OnClose) : BaseDialogRequest(OnClose);
}
