using System.ComponentModel.DataAnnotations;
using KHost.UserInterface.Models;

namespace KHost.UnitTests.UserInterface.Models;

public class EditTipModelTests
{
    [Fact]
    public void Validate_WithNoSinger_Fails()
    {
        var results = Validate(new EditTipModel { UserId = null, AmountInCents = 500 });

        Assert.Contains(results, r => r.ErrorMessage == "Singer is required.");
    }

    [Fact]
    public void Validate_WithASinger_PassesTheSingerRule()
    {
        var results = Validate(new EditTipModel { UserId = Guid.NewGuid(), AmountInCents = 500 });

        Assert.Empty(results);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10_000_000)]
    public void Validate_WithAnAmountOutsideTheRange_Fails(int cents)
    {
        var results = Validate(new EditTipModel { UserId = Guid.NewGuid(), AmountInCents = cents });

        Assert.Contains(results, r => r.ErrorMessage!.StartsWith("Amount must be between"));
    }

    private static List<ValidationResult> Validate(EditTipModel model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }
}
