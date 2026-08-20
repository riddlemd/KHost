using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace KHost.UserInterface.Components.Dialogs;

public partial class EditTipDialog : IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private const string _rootClassName = "kh-tip-edit-dialog";

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public Tip? Tip { get; set; }
    [Parameter] public Guid? UserId { get; set; }
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public bool CloseOnScrimClick { get; set; }

    [Parameter] public EventCallback<Tip> OnSave { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public EventCallback OnOpen { get; set; }

    [Inject] private IUsersService? UsersService { get; set; }

    private ElementReference _amountRef;
    private IJSObjectReference? _currencyModule;
    private IJSObjectReference? _currencyHandle;

    private bool _isNew;
    private EditTipModel _model = new();
    private EditContext _editContext = default!;
    private bool _prevIsOpen;
    private List<KHostUser> _users = [];

    protected override void OnInitialized()
    {
        _editContext = new EditContext(_model);
    }

    protected override async Task OnInitializedAsync()
    {
        if (UsersService != null && UserId is null)
        {
            var result = await UsersService.ReadAllAsync(pageSize: 1000);
            _users = result.Items.OrderBy(u => u.Name).ToList();
        }
    }

    protected override void OnParametersSet()
    {
        if (IsOpen && !_prevIsOpen)
        {
            _isNew = Tip is null;
            _model = Tip is null
                ? new EditTipModel()
                : new EditTipModel
                {
                    Id = Tip.Id,
                    UserId = Tip.UserId,
                    VenueId = Tip.VenueId,
                    AmountInCents = Tip.AmountInCents,
                    PaymentMethod = Tip.PaymentMethod,
                    Notes = Tip.Notes
                };

            if (UserId.HasValue)
                _model.UserId = UserId.Value;

            _editContext = new EditContext(_model);
        }
        _prevIsOpen = IsOpen;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _currencyModule = await JS.InvokeAsync<IJSObjectReference>("import", "/js/currency-input.js");
            _currencyHandle = await _currencyModule.InvokeAsync<IJSObjectReference>("init", _amountRef);
        }
        await _currencyModule!.InvokeVoidAsync("setValue", _amountRef, _model.AmountInCents);
    }

    private async Task SubmitAsync()
    {
        if (_editContext.Validate())
            await SaveAsync();
    }

    private async Task SaveAsync()
    {
        var tip = Tip ?? new Tip { Id = _model.Id, UserId = _model.UserId };
        tip.UserId = _model.UserId;
        tip.VenueId = _model.VenueId;
        tip.AmountInCents = _model.AmountInCents;
        tip.PaymentMethod = _model.PaymentMethod;
        tip.Notes = _model.Notes;

        await OnSave.InvokeAsync(tip);

        await CloseAsync();
    }

    private Task OnAmountChangedAsync(ChangeEventArgs e)
    {
        var digits = System.Text.RegularExpressions.Regex.Replace(e.Value?.ToString() ?? "", @"[^\d]", "");
        _model.AmountInCents = int.TryParse(digits, out var cents) ? cents : 0;
        _editContext.NotifyFieldChanged(FieldIdentifier.Create(() => _model.AmountInCents));
        return Task.CompletedTask;
    }

    public async Task CloseAsync()
    {
        IsOpen = false;

        await OnClose.InvokeAsync();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        if (_currencyHandle is not null) await _currencyHandle.InvokeVoidAsync("dispose");
        if (_currencyHandle is not null) await _currencyHandle.DisposeAsync();
        if (_currencyModule is not null) await _currencyModule.DisposeAsync();
    }

    public record DialogRequest : EditDialogRequest<Tip>
    {
        public Guid? UserId { get; init; }

        public DialogRequest(Tip? value, Action<Tip?> onSave, Action? onCancel, Action? onClose) : base(value, onSave, onCancel, onClose)
        {
        }

        public DialogRequest(Tip? value, Guid? lockedUserId, Action<Tip?> onSave, Action? onCancel, Action? onClose) : base(value, onSave, onCancel, onClose)
        {
            UserId = lockedUserId;
        }
    }
}
