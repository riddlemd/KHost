using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components;

public partial class HelpButton
{
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private Task OpenAsync() => DialogService.ShowHelpAsync();
}
