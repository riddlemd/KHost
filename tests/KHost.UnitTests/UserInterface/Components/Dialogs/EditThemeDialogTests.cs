using Bunit;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

public class EditThemeDialogTests : BunitContext
{
    public EditThemeDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(Substitute.For<IThemeService>());
    }

    /// <summary>The swatch and the text beside it are one value: picking in one fills the other.</summary>
    [Fact]
    public void PickingASwatch_FillsItsField()
    {
        var field = ThemeVariableCatalog.Fields.First(f => f.Kind == ThemeVariableKind.Color);
        var cut = Render<EditThemeDialog>(ps => ps.Add(p => p.IsOpen, true));

        cut.Find($"input[aria-label='{field.Label} colour picker']").Change("#ff0000");

        Assert.Equal("#ff0000", cut.Find($"#theme-field-{field.Key.TrimStart('-')}").GetAttribute("value"));
    }
}
