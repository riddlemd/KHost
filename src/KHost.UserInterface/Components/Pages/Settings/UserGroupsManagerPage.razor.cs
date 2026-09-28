using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class UserGroupsManagerPage : IDisposable
{
    [Inject] private IUserGroupsService UserGroupsService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private IVenuesService VenuesService { get; set; } = default!;
    [Inject] private IAppSettingsService AppSettingsService { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private PagedSearch<KHostUserGroup> _search = default!;

    protected override async Task OnInitializedAsync()
    {
        _subscriptions.Add(Broker.Subscribe<UserGroupsChanged>(OnStateChanged));

        _search = new PagedSearch<KHostUserGroup>(UserGroupsService.SearchAsync)
        { Size = AppSettingsService.Current.UserGroupsPageSize };

        await _search.SearchAsync();
    }

    private async Task OpenAddDialogAsync()
    {
        await DialogService.RequestEditAsync(new KHostUserGroup { Name = "" }, async group => await SaveAsync(group));
    }

    private async Task OpenEditDialogAsync(KHostUserGroup group)
    {
        await DialogService.RequestEditAsync(group, async updated => await SaveAsync(updated));
    }

    private async Task SaveAsync(KHostUserGroup? group)
    {
        if (group is null)
            return;

        var existing = await UserGroupsService.ReadAsync(group.Id);
        if (existing is null)
            await UserGroupsService.CreateAsync(group);
        else
            await UserGroupsService.UpdateAsync(group);
    }

    // Always confirmed: deleting a group affects every user in it, not just the row clicked.
    private async Task StartDeleteAsync(KHostUserGroup group)
    {
        await DialogService.ShowConfirmationAsync(
            $"Are you sure you want to delete <span class=\"kh-emphasis\">{group.Name}</span>?",
            async () => await UserGroupsService.DeleteAsync(group.Id),
            "Delete Group",
            "Delete"
        );
    }

    private void OnStateChanged(UserGroupsChanged message)
        => _ = InvokeAsync(async () =>
        {
            await _search.ReloadClampedAsync();
            StateHasChanged();
        });

    public void Dispose() => _subscriptions.Dispose();
}
