namespace KHost.Abstractions.Models;

public class Tip : RepositoryModel
{
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public Guid UserId { get; set; }
    public Guid? VenueId { get; set; }
    /// <summary>Whole cents. Stored as an INTEGER so it sorts and sums correctly in SQL.</summary>
    public int AmountInCents { get; set; }
    public TipPaymentMethod PaymentMethod { get; set; }
    public string Notes { get; set; } = "";

    /// <summary>The notes as search matches them. Written by the persistence layer, not by hand.</summary>
    public string NotesFolded { get; set; } = string.Empty;
}
