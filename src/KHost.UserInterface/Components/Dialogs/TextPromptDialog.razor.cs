using KHost.Abstractions.Interactions.Requests;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

public partial class TextPromptDialog
{
    private const string _rootClassName = "kh-text-prompt-dialog";

    private readonly Dictionary<string, string> _values = [];

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public string Title { get; set; } = "";
    [Parameter] public string? Message { get; set; }
    [Parameter] public IReadOnlyList<TextPromptField> Fields { get; set; } = [];
    [Parameter] public string Class { get; set; } = "";

    [Parameter] public EventCallback<IReadOnlyDictionary<string, string>> OnSubmit { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private string Value(string key) => _values.TryGetValue(key, out var value) ? value : "";

    private void SetValue(string key, string value) => _values[key] = value;

    private bool AllFieldsFilled() => Fields.All(field => !string.IsNullOrWhiteSpace(Value(field.Key)));

    private string FieldId(TextPromptField field) => $"{_rootClassName}-{field.Key}";

    private async Task SubmitAsync()
    {
        if (!AllFieldsFilled())
            return;

        await OnSubmit.InvokeAsync(_values);
        await CloseAsync();
    }

    public async Task CloseAsync()
    {
        IsOpen = false;

        await OnClose.InvokeAsync();
    }

    public record DialogRequest : BaseDialogRequest
    {
        public DialogRequest(
            string title, string? message, IReadOnlyList<TextPromptField> fields,
            Func<IReadOnlyDictionary<string, string>, Task> onSubmit, Action? onCancel, Action? onClose)
            : base(onClose)
        {
            Title = title;
            Message = message;
            Fields = fields;
            OnSubmit = onSubmit;
            OnCancel = onCancel;
        }

        public string Title { get; }
        public string? Message { get; }
        public IReadOnlyList<TextPromptField> Fields { get; }
        public Func<IReadOnlyDictionary<string, string>, Task> OnSubmit { get; }
        public Action? OnCancel { get; }
    }
}
