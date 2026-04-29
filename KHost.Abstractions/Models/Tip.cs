namespace KHost.Abstractions.Models;

public class Tip : RepositoryModel
{
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public Guid UserId { get; set; }
    public Guid? VenueId { get; set; }
    public decimal Amount { get; set; }
    public TipPaymentMethod PaymentMethod { get; set; }
    public string Notes { get; set; } = "";
}
