using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace KHost.UserInterface.Components.Dialogs;

public partial class EditUserDialog
{
    private const string _rootClassName = "kh-user-edit-dialog";

    [Inject] private IUserGroupsService UserGroupsService { get; set; } = default!;
    [Inject] private IUsersService UsersService { get; set; } = default!;
    [Inject] private IPerformanceService PerformanceService { get; set; } = default!;
    [Inject] private IMediaService MediaService { get; set; } = default!;
    [Inject] private IVenuesService VenuesService { get; set; } = default!;
    [Inject] private ITipsService TipsService { get; set; } = default!;
    [Inject] private IPasswordHasher PasswordHasher { get; set; } = default!;

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public KHostUser? User { get; set; }

    [Parameter] public EventCallback<KHostUser> OnSave { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private EditUserModel _model = new();
    private EditContext _editContext = default!;
    private List<KHostUserGroup> _availableGroups = [];
    private bool _isExistingUser;
    private string _newPassword = "";
    private bool _hasPassword;
    private int _totalTips;
    private List<RecentVenue> _recentVenues = [];
    private List<RecentSong> _recentSongs = [];

    private sealed record RecentVenue(string Name, DateTime LastSungOn);
    private sealed record RecentSong(string Title, string Artist, DateTime SungOn);

    // DialogHost keys every dialog by request id, so a fresh instance is created per open; this
    // runs exactly once with User already bound.
    protected override async Task OnInitializedAsync()
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
        _newPassword = "";
        _hasPassword = !string.IsNullOrEmpty(User?.PasswordHash);

        await LoadGroupsAsync();
        await LoadStatsAsync();
    }

    private const int StatsCount = 5;

    private async Task LoadStatsAsync()
    {
        _isExistingUser = false;
        _totalTips = 0;
        _recentVenues = [];
        _recentSongs = [];

        if (User is null) return;

        // The add flow hands us an unsaved KHostUser, so identity alone cannot tell the two apart.
        // Only a round trip can, and stats would be empty for a user who does not exist yet.
        if (await UsersService.ReadAsync(User.Id) is null) return;

        _isExistingUser = true;

        _totalTips = await TipsService.GetTotalInCentsByUserIdAsync(User.Id);

        var visits = await PerformanceService.ReadRecentVenueVisitsBySingerAsync(User.Id, StatsCount);
        var venues = await Task.WhenAll(visits.Select(v => VenuesService.ReadAsync(v.VenueId)));

        // A deleted venue leaves the visit unresolvable, so drop it rather than showing a blank row.
        _recentVenues = [.. visits
            .Select((visit, i) => (Venue: venues[i], visit.LastSungOn))
            .Where(x => x.Venue is not null)
            .Select(x => new RecentVenue(x.Venue!.Name, x.LastSungOn))];

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

    // pageSize 0 is not "unpaged"; it falls back to the repository default of 50 and would
    // silently hide groups from the picker.
    private const int GroupPageSize = 1000;

    private async Task LoadGroupsAsync()
    {
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

    private async Task SaveAsync()
    {
        if (!_editContext.Validate()) return;

        var user = User ?? new KHostUser { Id = _model.Id, Name = _model.Name };
        user.Name = _model.Name;
        user.Notes = _model.Notes;
        user.Groups = [.. _availableGroups.Where(g => _model.SelectedGroupIds.Contains(g.Id))];

        if (!string.IsNullOrWhiteSpace(_newPassword))
            user.PasswordHash = await PasswordHasher.HashAsync(_newPassword);

        // DialogHost closes after awaiting this itself; closing again here would also fire
        // OnClose's onCancel, marking a successful save as a cancel.
        await OnSave.InvokeAsync(user);
    }

    public record DialogRequest : EditDialogRequest<KHostUser>
    {
        public DialogRequest(KHostUser? value, Func<KHostUser?, Task> onSave, Action? onCancel, Action? onClose) : base(value, onSave, onCancel, onClose)
        {
        }
    }
}
