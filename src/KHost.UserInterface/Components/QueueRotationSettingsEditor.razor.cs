using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.QueueRotation;
using KHost.Abstractions.Models.QueueRotation;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components;

public partial class QueueRotationSettingsEditor
{
    [Inject] private IQueueRotationStrategyFactory? StrategyFactory { get; set; }
    [Inject] private IUserGroupsService? UserGroupsService { get; set; }

    [Parameter, EditorRequired] public QueueRotationConfig Config { get; set; } = new();

    private IReadOnlyList<IQueueRotationMode> _modes = [];
    private List<KHostUserGroup> _groups = [];

    private string ModeDescription => _modes.FirstOrDefault(m => m.Id == Config.StrategyId)?.Description ?? "";

    private string VipGroupIdString
    {
        get => Config.VipGroupId?.ToString() ?? "";
        set => Config.VipGroupId = Guid.TryParse(value, out var id) ? id : null;
    }

    protected override async Task OnInitializedAsync()
    {
        if (StrategyFactory is null || UserGroupsService is null)
            return;

        _modes = StrategyFactory.GetAllModes();

        var groups = await UserGroupsService.ReadAllAsync(pageSize: 1000);
        _groups = groups.Items.OrderBy(g => g.Name).ToList();
    }
}
