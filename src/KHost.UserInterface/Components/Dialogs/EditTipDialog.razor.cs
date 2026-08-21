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
    [Inject] private IUsersService? UsersService { get; set; }

    private const string _rootClassName = "kh-tip-edit-dialog";

    private const int SingerResultLimit = 20;

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public Tip? Tip { get; set; }
    [Parameter] public Guid? UserId { get; set; }

    [Parameter] public string Class { get; set; } = "";
    [Parameter] public bool CloseOnScrimClick { get; set; }

    [Parameter] public EventCallback<Tip> OnSave { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public EventCallback OnOpen { get; set; }

    private ElementReference _amountRef;
    private IJSObjectReference? _currencyModule;
    private IJSObjectReference? _currencyHandle;

    private bool _isNew;
    private KHostUser? _singer;
    private EditTipModel _model = new();
    private EditContext _editContext = default!;
    private bool _prevIsOpen;
    // What was last written into the input, so a re-render never overwrites what is being typed.
    private int? _pushedCents;

    protected override void OnInitialized()
    {
        _editContext = new EditContext(_model);
    }

    protected override async Task OnParametersSetAsync()
    {
        if (IsOpen && !_prevIsOpen)
        {
            _isNew = Tip is null;
            _model = Tip is null
                ? new EditTipModel()
                : new EditTipModel
                {
                    Id = Tip.Id,
                    // The singer-locked overload can hand over a blank tip, and Guid.Empty has to
                    // read as "not chosen" for [Required] to fail on it.
                    UserId = Tip.UserId == Guid.Empty ? null : Tip.UserId,
                    VenueId = Tip.VenueId,
                    AmountInCents = Tip.AmountInCents,
                    PaymentMethod = Tip.PaymentMethod,
                    Notes = Tip.Notes
                };

            if (UserId.HasValue)
                _model.UserId = UserId.Value;

            _editContext = new EditContext(_model);
            _pushedCents = null;

            // A stored tip carries an id; the field shows a name.
            _singer = _model.UserId is null || UsersService is null
                ? null
                : await UsersService.ReadAsync(_model.UserId.Value);
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
        // Only when the value actually changed underneath us. Pushing on every render put the
        // model's value back over whatever was half-typed, and the next change event reported that.
        if (_pushedCents != _model.AmountInCents)
        {
            _pushedCents = _model.AmountInCents;
            await _currencyModule!.InvokeVoidAsync("setValue", _amountRef, _model.AmountInCents);
        }
    }

    private async Task SubmitAsync()
    {
        if (_editContext.Validate())
            await SaveAsync();
    }

    private async Task SaveAsync()
    {
        // Validation has run, so the singer is set.
        var userId = _model.UserId!.Value;

        var tip = Tip ?? new Tip { Id = _model.Id, UserId = userId };
        tip.UserId = userId;
        tip.VenueId = _model.VenueId;
        tip.AmountInCents = _model.AmountInCents;
        tip.PaymentMethod = _model.PaymentMethod;
        tip.Notes = _model.Notes;

        await OnSave.InvokeAsync(tip);

        await CloseAsync();
    }

    private async Task<IReadOnlyList<KHostUser>> SearchSingersAsync(string query)
    {
        if (UsersService is null) return [];

        var result = await UsersService.SearchAsync(query, 1, SingerResultLimit);

        return result.Items;
    }

    private Task OnSingerChangedAsync(KHostUser? singer)
    {
        _singer = singer;
        _model.UserId = singer?.Id;
        _editContext.NotifyFieldChanged(FieldIdentifier.Create(() => _model.UserId));

        return Task.CompletedTask;
    }

    private Task OnAmountChangedAsync(ChangeEventArgs e)
    {
        var digits = System.Text.RegularExpressions.Regex.Replace(e.Value?.ToString() ?? "", @"[^\d]", "");
        _model.AmountInCents = int.TryParse(digits, out var cents) ? cents : 0;
        // The input already shows what was typed; re-pushing it would fight the caret.
        _pushedCents = _model.AmountInCents;
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

        public DialogRequest(Tip? value, Func<Tip?, Task> onSave, Action? onCancel, Action? onClose) : base(value, onSave, onCancel, onClose)
        {
        }

        public DialogRequest(Tip? value, Guid? lockedUserId, Func<Tip?, Task> onSave, Action? onCancel, Action? onClose)
            : base(value, onSave, onCancel, onClose)
        {
            UserId = lockedUserId;
        }
    }
}
