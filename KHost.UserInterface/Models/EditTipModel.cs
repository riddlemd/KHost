namespace KHost.UserInterface.Models;

using KHost.Abstractions.Models;
using System.ComponentModel.DataAnnotations;

public class EditTipModel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "Singer is required.")]
    public Guid UserId { get; set; }

    public Guid? VenueId { get; set; }

    [Required(ErrorMessage = "Amount is required.")]
    [Range(0.01, 99999.99, ErrorMessage = "Amount must be between $0.01 and $99,999.99.")]
    public decimal Amount { get; set; }

    public TipPaymentMethod PaymentMethod { get; set; } = TipPaymentMethod.Cash;

    [MaxLength(1000, ErrorMessage = "Notes cannot exceed 1000 characters.")]
    public string Notes { get; set; } = "";
}
