namespace KHost.Abstractions.Models;

/// <summary>One tip a singer or guest left, for a venue or unattached to one.</summary>
public class Tip : RepositoryModel
{
    /// <summary>When the tip was recorded, in UTC.</summary>
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    /// <summary>Who received it.</summary>
    public Guid UserId { get; set; }

    /// <summary>Which venue it was left at. Null for a tip not tied to a venue.</summary>
    public Guid? VenueId { get; set; }

    /// <summary>Whole cents, not dollars — e.g. 550 means $5.50.</summary>
    public int AmountInCents { get; set; }

    /// <summary>How the tip was paid.</summary>
    public TipPaymentMethod PaymentMethod { get; set; }

    /// <summary>Free-form note about the tip.</summary>
    public string Notes { get; set; } = "";

    /// <summary>A search-friendly form of <see cref="Notes"/>, computed by the host — do not set
    /// this directly.</summary>
    public string NotesFolded { get; set; } = string.Empty;
}
