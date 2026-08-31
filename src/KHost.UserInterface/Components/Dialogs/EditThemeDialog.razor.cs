using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace KHost.UserInterface.Components.Dialogs;

public partial class EditThemeDialog
{
    private const string _rootClassName = "kh-theme-edit-dialog";

    [Inject] private IThemeService? ThemeService { get; set; }

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public ThemeDefinition? Theme { get; set; }
    [Parameter] public bool CloseOnScrimClick { get; set; }
    [Parameter] public string Class { get; set; } = "";

    [Parameter] public EventCallback<ThemeDefinition> OnSave { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private EditThemeModel _model = new();
    private EditContext _editContext = default!;
    private bool _prevIsOpen;

    protected override void OnInitialized()
    {
        _model.Values = ThemeVariableCatalog.Defaults();
        _editContext = new EditContext(_model);
    }

    protected override async Task OnParametersSetAsync()
    {
        if (IsOpen && !_prevIsOpen)
        {
            _model = new EditThemeModel
            {
                Id = Theme?.Id ?? "",
                Name = Theme?.Name ?? "",
                IsEnabled = Theme?.IsEnabled ?? true,
                // Resolved through the service rather than off the definition so a theme stored
                // before a variable existed still opens with every field filled in.
                Values = Theme is null || ThemeService is null
                    ? ThemeVariableCatalog.Defaults()
                    : await ThemeService.ReadVariablesAsync(Theme.Id)
            };

            _editContext = new EditContext(_model);
        }

        _prevIsOpen = IsOpen;
    }

    private static string FieldId(ThemeVariable field) => $"theme-field-{field.Key.TrimStart('-')}";

    private string Value(ThemeVariable field)
        => _model.Values.TryGetValue(field.Key, out var value) ? value : field.Fallback;

    /// <summary>A colour input only accepts a hex literal, so anything else shows the field's own default.</summary>
    private string Swatch(ThemeVariable field)
    {
        var value = Value(field);

        if (!ThemeCss.TryParseHex(value, out var r, out var g, out var b)
            && !ThemeCss.TryParseHex(field.Fallback, out r, out g, out b))
            return "#000000";

        return $"#{r:x2}{g:x2}{b:x2}";
    }

    private void DeriveShades()
    {
        ThemeCss.DeriveShades(_model.Values);
        _editContext.Validate();
    }

    private void Set(string key, string? value)
    {
        _model.Values[key] = value?.Trim() ?? "";
        _editContext.Validate();
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

        var theme = new ThemeDefinition
        {
            Id = _model.Id,
            Name = _model.Name.Trim(),
            IsBuiltIn = false,
            IsEnabled = _model.IsEnabled,
            Variables = new Dictionary<string, string>(_model.Values, StringComparer.Ordinal)
        };

        await OnSave.InvokeAsync(theme);
        await CloseAsync();
    }

    public record DialogRequest : EditDialogRequest<ThemeDefinition>
    {
        public DialogRequest(ThemeDefinition? value, Func<ThemeDefinition?, Task> onSave, Action? onCancel, Action? onClose)
            : base(value, onSave, onCancel, onClose)
        {
        }
    }
}
