using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace KHost.UserInterface.Components.Pages.Settings;

/// <summary>A bound select built from a fixed choice list, for rows whose options are generated rather than literal.</summary>
public partial class ChoiceSelect<TValue>
{
    [Parameter] public string? Id { get; set; }
    [Parameter] public string Class { get; set; } = "kh-form-select kh-app-settings__number";
    [Parameter, EditorRequired] public IReadOnlyList<TValue> Choices { get; set; } = [];
    [Parameter, EditorRequired] public TValue Value { get; set; } = default!;
    [Parameter] public EventCallback<TValue> ValueChanged { get; set; }
    [Parameter, EditorRequired] public Func<TValue, string> Label { get; set; } = default!;

    // Every option comes from Choices, so a direct convert is safe without pulling in a generic parser.
    // Convert.ChangeType cannot make an enum from its name, so an enum is parsed.
    private Task OnChangeAsync(ChangeEventArgs e)
    {
        var text = e.Value?.ToString() ?? "";
        var value = typeof(TValue).IsEnum
            ? (TValue)Enum.Parse(typeof(TValue), text)
            : (TValue)Convert.ChangeType(text, typeof(TValue));

        return ValueChanged.InvokeAsync(value);
    }
}
