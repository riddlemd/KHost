namespace KHost.UserInterface.Models;

using KHost.Abstractions.Models;
using System.ComponentModel.DataAnnotations;

public class EditTipModel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Nullable so [Required] can fail: it rejects only null, and Guid.Empty satisfies it.
    [Required(ErrorMessage = "Singer is required.")]
    public Guid? UserId { get; set; }

    public Guid? VenueId { get; set; }

    [Required(ErrorMessage = "Amount is required.")]
    [Range(1, 9_999_999, ErrorMessage = "Amount must be between $0.01 and $99,999.99.")]
    public int AmountInCents { get; set; }

    /// <summary>Local time, because that is what the host is reading off a clock. Stored as UTC.</summary>
    public DateTime CreatedDate { get; set; } = DateTime.Now;

    public TipPaymentMethod PaymentMethod { get; set; } = TipPaymentMethod.Cash;

    [MaxLength(1000, ErrorMessage = "Notes cannot exceed 1000 characters.")]
    public string Notes { get; set; } = "";
}
