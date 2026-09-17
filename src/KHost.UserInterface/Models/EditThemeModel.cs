using System.ComponentModel.DataAnnotations;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Models;

public class EditThemeModel : IValidatableObject
{
    public string Id { get; set; } = "";

    [Required(ErrorMessage = "Name is required.")]
    [MaxLength(60, ErrorMessage = "Name cannot exceed 60 characters.")]
    public string Name { get; set; } = "";

    public bool IsEnabled { get; set; } = true;

    public Dictionary<string, string> Values { get; set; } = [];

    /// <summary>Checked here as well as at render time, since values are free-form.</summary>
    /// <remarks>The dialog can say which field is wrong; the renderer only drops it.</remarks>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var field in ThemeVariableCatalog.Fields)
        {
            Values.TryGetValue(field.Key, out var value);

            if (!ThemeCss.IsValidValue(value))
                yield return new ValidationResult($"{field.Label} is not a usable value.");
            else if (!ThemeCss.IsValidFor(field, value))
                yield return new ValidationResult($"{field.Label} has to be a hex colour, such as #5D2B90.");
        }
    }
}
