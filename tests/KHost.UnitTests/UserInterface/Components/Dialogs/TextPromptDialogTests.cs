using Bunit;
using KHost.Abstractions.Interactions.Requests;
using KHost.UserInterface.Components.Dialogs;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

public class TextPromptDialogTests : BunitContext
{
    private static readonly IReadOnlyList<TextPromptField> LoginFields =
    [
        new("login", "Email"),
        new("password", "Password", Secret: true),
    ];

    public TextPromptDialogTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void Render_OneInputPerField_SecretFieldsAreMasked()
    {
        var cut = Render(LoginFields);

        var login = cut.Find("#kh-text-prompt-dialog-login");
        var password = cut.Find("#kh-text-prompt-dialog-password");

        Assert.Equal("text", login.GetAttribute("type"));
        Assert.Equal("password", password.GetAttribute("type"));
    }

    [Fact]
    public void Render_NoMessage_ShowsNoMessageParagraph()
        => Assert.Empty(Render(LoginFields, message: null).FindAll(".kh-text-prompt-dialog__message"));

    [Fact]
    public void Render_WithMessage_ShowsIt()
        => Assert.Equal("Sign in to search.", Render(LoginFields, message: "Sign in to search.")
            .Find(".kh-text-prompt-dialog__message").TextContent);

    [Fact]
    public void SubmitButton_FieldsBlank_IsDisabled()
        => Assert.True(Render(LoginFields).Find(".kh-text-prompt-dialog__submit-btn").HasAttribute("disabled"));

    [Fact]
    public void SubmitButton_OnlySomeFieldsFilled_StaysDisabled()
    {
        var cut = Render(LoginFields);

        cut.Find("#kh-text-prompt-dialog-login").Input("host@example.com");

        Assert.True(cut.Find(".kh-text-prompt-dialog__submit-btn").HasAttribute("disabled"));
    }

    [Fact]
    public void SubmitButton_EveryFieldFilled_IsEnabled()
    {
        var cut = Render(LoginFields);

        cut.Find("#kh-text-prompt-dialog-login").Input("host@example.com");
        cut.Find("#kh-text-prompt-dialog-password").Input("hunter2");

        Assert.False(cut.Find(".kh-text-prompt-dialog__submit-btn").HasAttribute("disabled"));
    }

    [Fact]
    public void Submit_PassesEachFieldsValueByKey()
    {
        IReadOnlyDictionary<string, string>? submitted = null;
        var cut = Render(LoginFields, onSubmit: values => { submitted = values; return Task.CompletedTask; });

        cut.Find("#kh-text-prompt-dialog-login").Input("host@example.com");
        cut.Find("#kh-text-prompt-dialog-password").Input("hunter2");
        cut.Find(".kh-text-prompt-dialog__submit-btn").Click();

        Assert.Equal("host@example.com", submitted?["login"]);
        Assert.Equal("hunter2", submitted?["password"]);
    }

    [Fact]
    public void Submit_ClosesTheDialog()
    {
        var closed = false;
        var cut = Render(LoginFields, onClose: () => closed = true);

        cut.Find("#kh-text-prompt-dialog-login").Input("host@example.com");
        cut.Find("#kh-text-prompt-dialog-password").Input("hunter2");
        cut.Find(".kh-text-prompt-dialog__submit-btn").Click();

        Assert.True(closed);
    }

    /// <summary>
    /// The hidden submit button that lets Enter submit the form is not disabled the way the visible
    /// one is, so the guard against a blank submission has to live in code, not in markup.
    /// </summary>
    [Fact]
    public void SubmitViaForm_FieldsBlank_DoesNotSubmit()
    {
        var submitted = false;
        var cut = Render(LoginFields, onSubmit: _ => { submitted = true; return Task.CompletedTask; });

        cut.Find("form").Submit();

        Assert.False(submitted);
    }

    [Fact]
    public void Cancel_ClosesWithoutSubmitting()
    {
        var submitted = false;
        var closed = false;
        var cut = Render(LoginFields, onSubmit: _ => { submitted = true; return Task.CompletedTask; }, onClose: () => closed = true);

        cut.Find(".kh-text-prompt-dialog__cancel-btn").Click();

        Assert.False(submitted);
        Assert.True(closed);
    }

    private IRenderedComponent<TextPromptDialog> Render(
        IReadOnlyList<TextPromptField> fields, string? message = null,
        Func<IReadOnlyDictionary<string, string>, Task>? onSubmit = null, Action? onClose = null)
        => Render<TextPromptDialog>(ps => ps
            .Add(p => p.IsOpen, true)
            .Add(p => p.Title, "KaraFun sign in")
            .Add(p => p.Message, message)
            .Add(p => p.Fields, fields)
            .Add(p => p.OnSubmit, onSubmit ?? (_ => Task.CompletedTask))
            .Add(p => p.OnClose, onClose ?? (() => { })));
}
