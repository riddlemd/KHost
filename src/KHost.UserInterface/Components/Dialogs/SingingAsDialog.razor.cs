using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using KHost.Abstractions.Models;
using KHost.UserInterface.Models;

namespace KHost.UserInterface.Components.Dialogs;

/// <summary>
/// Changes the name one turn is queued under, without touching the singer it belongs to.
/// </summary>
/// <remarks>
/// Its own dialog rather than a field on an edit-the-performance form, because a turn has nothing
/// else a host would change: the song and the singer are what the row already is, and moving either
/// is a different action with its own control.
/// </remarks>
public partial class SingingAsDialog
{
    private const string _rootClassName = "kh-singing-as-dialog";

    private SingingAsModel _model = new();
    private EditContext _editContext = new(new SingingAsModel());
    private bool _prevIsOpen;

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public Performance? Performance { get; set; }

    /// <summary>The singer's own name, which is what a blank field falls back to.</summary>
    [Parameter] public string? SingerName { get; set; }

    [Parameter] public string Class { get; set; } = "";
    [Parameter] public bool CloseOnScrimClick { get; set; }
    [Parameter] public EventCallback<Performance> OnSave { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private string _fallbackName =>
        string.IsNullOrWhiteSpace(SingerName) ? "the singer’s own name" : SingerName!;

    protected override void OnParametersSet()
    {
        // Only on the way open. Rebuilding on every parameter change would throw away what the host
        // has typed the moment anything else on the page re-renders.
        if (IsOpen && !_prevIsOpen)
        {
            _model = new SingingAsModel { SungAs = Performance?.SungAs };
            _editContext = new EditContext(_model);
        }

        _prevIsOpen = IsOpen;
    }

    private async Task SubmitAsync()
    {
        if (_editContext.Validate())
            await SaveAsync();
    }

    private async Task SaveAsync()
    {
        if (Performance is null)
        {
            await CloseAsync();
            return;
        }

        var typed = _model.SungAs?.Trim();

        // Blank is stored as null, not as "": every reader treats an empty recorded name as "use the
        // singer's own", and null is the same answer a row that was never given one carries.
        Performance.SungAs = string.IsNullOrEmpty(typed) ? null : typed;

        await OnSave.InvokeAsync(Performance);
        await CloseAsync();
    }

    private async Task CloseAsync()
    {
        IsOpen = false;
        await OnClose.InvokeAsync();
    }

    private sealed class SingingAsModel
    {
        /// <summary>Matched to the column, which the migration cut at 255.</summary>
        [MaxLength(255, ErrorMessage = "That name is too long.")]
        public string? SungAs { get; set; }
    }

    public record DialogRequest : EditDialogRequest<Performance>
    {
        public DialogRequest(
            Performance? value, string? singerName, Func<Performance?, Task> onSave,
            Action? onCancel, Action? onClose)
            : base(value, onSave, onCancel, onClose)
        {
            SingerName = singerName;
        }

        public string? SingerName { get; init; }
    }
}
