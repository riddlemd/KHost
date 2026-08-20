using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Setup;

public partial class WizardStepSecurity
{
    [Parameter] public EventCallback<bool> OnComplete { get; set; }

    private bool _requireLogin = true;

    private Task OnNextAsync() => OnComplete.InvokeAsync(_requireLogin);
}
