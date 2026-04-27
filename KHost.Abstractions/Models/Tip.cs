namespace KHost.Abstractions.Models;

public class Tip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public Guid UserId { get; set; }
    public Guid? VenueId { get; set; }
    public decimal Amount { get; set; }
    public TipPaymentMethod PaymentMethod { get; set; }
    public string Notes { get; set; } = "";
}
