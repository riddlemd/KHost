using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace KHost.UserInterface.Components.Dialogs;

public partial class EditUserDialog
{
    private const string _rootClassName = "kh-user-edit-dialog";

    [Inject] private IUserGroupsService? UserGroupsService { get; set; }
    [Inject] private IUsersService? UsersService { get; set; }
    [Inject] private IPerformanceService? PerformanceService { get; set; }
    [Inject] private IMediaService? MediaService { get; set; }
    [Inject] private IVenuesService? VenuesService { get; set; }
    [Inject] private ITipsService? TipsService { get; set; }

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public KHostUser? User { get; set; }
    [Parameter] public bool CloseOnScrimClick { get; set; }
    [Parameter] public string Class { get; set; } = "";

    [Parameter] public EventCallback<KHostUser> OnSave { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private EditUserModel _model = new();
    private EditContext _editContext = default!;
    private bool _prevIsOpen;
    private List<KHostUserGroup> _availableGroups = [];
    private bool _isExistingUser;
    private decimal _totalTips;
    private List<RecentVenue> _recentVenues = [];
    private List<RecentSong> _recentSongs = [];

    private sealed record RecentVenue(string Name, DateTime LastSungOn);
    private sealed record RecentSong(string Title, string Artist, DateTime SungOn);

    protected override void OnInitialized()
    {
        _editContext = new EditContext(_model);
    }

    protected override async Task OnParametersSetAsync()
    {
        if (IsOpen && !_prevIsOpen)
        {
            _model = User is null
                    ? new EditUserModel()
                    : new EditUserModel
                    {
                        Id = User.Id,
                        Name = User.Name,
                        Notes = User.Notes,
                        SelectedGroupIds = User.Groups.Select(g => g.Id).ToList()
                    };

            _editContext = new EditContext(_model);

            await LoadGroupsAsync();
            await LoadStatsAsync();
        }
        _prevIsOpen = IsOpen;
    }

    private const int StatsCount = 5;

    private async Task LoadStatsAsync()
    {
        _isExistingUser = false;
        _totalTips = 0;
        _recentVenues = [];
        _recentSongs = [];

        if (User is null || UsersService is null || PerformanceService is null) return;

        // The add flow hands us an unsaved KHostUser, so identity alone cannot tell the two apart —
        // only a round trip can, and stats would be empty for a user who does not exist yet.
        if (await UsersService.ReadAsync(User.Id) is null) return;

        _isExistingUser = true;

        if (TipsService is not null)
            _totalTips = await TipsService.GetTotalByUserIdAsync(User.Id);

        if (VenuesService is not null)
        {
            var visits = await PerformanceService.ReadRecentVenueVisitsBySingerAsync(User.Id, StatsCount);
            var venues = await Task.WhenAll(visits.Select(v => VenuesService.ReadAsync(v.VenueId)));

            // A deleted venue leaves the visit unresolvable, so drop it rather than showing a blank row.
            _recentVenues = [.. visits
                .Select((visit, i) => (Venue: venues[i], visit.LastSungOn))
                .Where(x => x.Venue is not null)
                .Select(x => new RecentVenue(x.Venue!.Name, x.LastSungOn))];
        }

        if (MediaService is not null)
        {
            var performances = await PerformanceService.ReadBySingerIdAsync(
                User.Id, pageNumber: 1, pageSize: StatsCount, PerformanceFilter.UnQueued);

            var media = await Task.WhenAll(performances.Items.Select(p => MediaService.ReadAsync(p.MediaId)));

            // A performance outlives the song being removed from the library, so the row stays and
            // says so rather than vanishing from the singer's history.
            _recentSongs = [.. performances.Items.Select((p, i) => new RecentSong(
                media[i]?.Title ?? "Song no longer in library",
                media[i]?.Artist ?? "",
                p.CreatedDate))];
        }
    }

    // pageSize 0 is not "unpaged" — it falls back to the repository default of 50 and would
    // silently hide groups from the picker.
    private const int GroupPageSize = 1000;

    private async Task LoadGroupsAsync()
    {
        if (UserGroupsService is null) return;

        var result = await UserGroupsService.ReadAllAsync(1, GroupPageSize);
        _availableGroups = [.. result.Items];
    }

    private void ToggleGroup(Guid groupId, bool selected)
    {
        if (selected)
        {
            if (!_model.SelectedGroupIds.Contains(groupId))
                _model.SelectedGroupIds.Add(groupId);
        }
        else
        {
            _model.SelectedGroupIds.Remove(groupId);
        }
    }

    public async Task CloseAsync()
    {
        IsOpen = false;

        await OnClose.InvokeAsync();
    }

    private async Task CancelAsync()
    {
        await OnClose.InvokeAsync();

        await CloseAsync();
    }

    private async Task SaveAsync()
    {
        if (!_editContext.Validate()) return;

        var user = User ?? new KHostUser { Id = _model.Id, Name = _model.Name };
        user.Name = _model.Name;
        user.Notes = _model.Notes;
        user.Groups = [.. _availableGroups.Where(g => _model.SelectedGroupIds.Contains(g.Id))];

        await OnSave.InvokeAsync(user);

        await CloseAsync();
    }

    public record DialogRequest : EditDialogRequest<KHostUser>
    {
        public DialogRequest(KHostUser? value, Action<KHostUser?> onSave, Action? onCancel, Action? onClose) : base(value, onSave, onCancel, onClose)
        {
        }
    }
}
